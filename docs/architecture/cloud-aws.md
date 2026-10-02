# Part C — Cloud deployment (AWS)

Diagram: [cloud-aws.html](cloud-aws.html). It builds on the Part B design in [microservices.md](microservices.md).

## 1. Requirements review

From the spec: *"Show in a short sketch how you would deploy the system to a cloud (AWS / Azure / GCP / other). Address the main components: Compute, DB, Messaging, Monitoring, Scaling. No need for deployment infrastructure-as-code."*

What that means for this system:

| Requirement | What it has to cover here |
|---|---|
| **Compute** | Run 5 .NET 8 services: 4 HTTP APIs plus background consumers (Notifications, Reporting) and the outbox relay. Also host the Angular SPA. |
| **DB** | One database per service (Part B rule). The Requests search must stay fast at **millions of rows** (Part A). Notifications needs dedupe. Documents needs file storage. |
| **Messaging** | Durable pub/sub with a queue per consumer, retries with backoff, and a DLQ, so notifications survive Notifications being down (Part B scenario). |
| **Monitoring** | See failures that don't show up as HTTP errors: queue backlog, DLQ depth, outbox lag. Trace one user action across services. |
| **Scaling** | Scale APIs on traffic, workers on backlog and DBs on read load. Survive the loss of an AZ, and plan for a region loss. |
| *Not required* | IaC. In practice this would be Terraform or CDK. |

**Scale assumptions ("massive"):** hundreds of millions of Requests rows, growing about 10M/month. Around 1M daily users, peaks of about 5k API calls/s, and event bursts of 10k/s (for example a bulk status change). The read/search to write ratio is about 20:1.

## 2. How the deployment answers each component

### Edge and identity
- **Route 53** for DNS, with health-check failover to a second region (see DR).
- **CloudFront + AWS WAF + Shield** in front of everything. The SPA is served from **S3** (private bucket, Origin Access Control), cached at the edge. `/api/*` goes to the ALB. WAF handles rate limiting, OWASP managed rules and bot control at the edge, before any of our compute runs.
- **Cognito** (or the organization's IdP via OIDC) issues JWTs. Each service validates the token itself (`JwtBearer` with cached JWKS) and applies the Part A authorization rules from the claims. This replaces the `X-User-Id` / `X-Is-Admin` headers.

### Compute: ECS on Fargate
- **One ECS service per microservice**, with tasks spread across **3 AZs**, inside private subnets. Only the ALB is public.
- **ALB** with path-based routing (`/api/requests` → Requests, and so on). It plays the "API Gateway" role from Part B. There is no AWS API Gateway product in this design; see [section 4](#4-decision-no-aws-api-gateway-alb--waf-instead).
- **ECS Service Connect** carries the internal sync REST calls (Requests → Customers, Notifications → Customers), with service discovery and retries/timeouts in the client (Part B resilience handler).
- The **outbox relay** runs as a hosted background service inside the Requests task. Only one relay should publish at a time, so it takes a Postgres advisory lock. Several relays would still be safe, since consumers dedupe, but they'd waste work.
- **Workers** (Notifications, Reporting consumers) are separate ECS services, so they scale independently of the APIs.

### DB: data store chosen per service
| Service | Store | Why |
|---|---|---|
| **Requests** | **Aurora PostgreSQL** (I/O-Optimized). 1 writer + 2–15 auto-scaled readers. **RDS Proxy** in front. | Relational search with the Part A indexes. Searches go to the **reader endpoint** and writes go to the writer. The table is **partitioned by month on `CreatedAt`** (pg_partman), so date-range filters prune partitions and old partitions can be archived. A `pg_trgm` GIN index serves partial `RequestNumber` search. RDS Proxy pools connections from hundreds of Fargate tasks. |
| **Customers** | Aurora PostgreSQL (small: writer + 1 reader) | Relational, low volume. |
| **Reporting** | Aurora PostgreSQL read model | Denormalized tables built from events. Heavy reports never touch Requests' DB. |
| **Notifications** | **DynamoDB** (on-demand) | A key-value log at high write volume. A **conditional put on `MessageId`** (`attribute_not_exists`) is the dedupe from Part B: atomic and cheap. |
| **Documents** | **S3** (files, SSE-KMS) + metadata in Aurora | The browser uploads directly with **pre-signed URLs**, so file bytes never pass through our compute. |
| Shared | **ElastiCache for Redis** (Multi-AZ) | `Idempotency-Key` responses (24h TTL) and hot lookups such as customer contact. |

Search at the next order of magnitude: when search load dominates the Requests DB, project events into **OpenSearch** (CQRS) and point `GET /requests` at it. If a single Aurora writer is ever the limit, move to **Aurora Limitless** (sharded Postgres) or shard by customer. Neither is needed on day one.

### Messaging: SNS + SQS
- **SNS topic per event type** (`request-events`, `customer-events`, `document-events`), with an **SQS standard queue per consumer** subscribed to it. This is fan-out without consumers knowing about each other.
- Mapping the Part B retry ladder onto SQS:
  - **Immediate retries:** 3 in-process tries per receive.
  - **Delayed redelivery:** the consumer sets `ChangeMessageVisibility` to 1m / 5m / 30m / 2h (the max is 12h) and doesn't delete the message.
  - **DLQ:** a redrive policy with `maxReceiveCount = 5` (1 normal receive + 4 delayed) moves the message to that queue's own DLQ.
- **Standard, not FIFO:** FIFO limits throughput, and ordering is already handled by the per-request `version` check in consumers.
- **MassTransit's Amazon SQS transport** implements this (outbox, retry, redelivery, DLQ), so the code from Part B doesn't change. Locally it runs on RabbitMQ; only the transport config differs.
- **SES** sends email (plus AWS End User Messaging for SMS).
- **VPC endpoints** for SNS, SQS, S3, DynamoDB, SES, ECR and Secrets Manager. Traffic stays private and no NAT gateway is needed.

### Monitoring
- **CloudWatch Logs** (structured JSON with `correlationId`) and **Container Insights** for ECS.
- **OpenTelemetry** (ADOT collector sidecar) sends traces to **X-Ray**. One trace follows the HTTP call → outbox → SNS → SQS → consumer → SES, because `correlationId` travels in the event envelope.
- **Alarms** (→ SNS → PagerDuty/Slack) on the signals that catch the Part B failure modes:

| Alarm | Catches |
|---|---|
| DLQ `ApproximateNumberOfMessagesVisible > 0` | Retries exhausted (scenario 5) |
| Queue `ApproximateAgeOfOldestMessage > 5 min` | Consumer down or too slow (scenario 2) |
| Custom metric `outbox_pending` / oldest unpublished age | Broker or relay problem (scenario 3) |
| ALB 5xx rate, p99 latency per target group | API errors |
| Circuit-breaker-open count (custom metric) | Sync dependency down (scenario 7) |
| Aurora CPU, replica lag, connections; DynamoDB throttles | DB pressure |

- **Dashboards** per service: golden signals (latency, traffic, errors, saturation) plus queue depth.

### Scaling
| Layer | How it scales |
|---|---|
| CloudFront / S3 | Managed; the static SPA is cached at the edge. |
| API services | ECS target tracking on **ALB requests per target** and CPU. Min 2–6 tasks (3 AZs), max 20–60. |
| Workers | Scale on **SQS backlog per task** (`visible messages ÷ running tasks`, target about 100). They scale out on a burst and in to the minimum when idle. |
| Aurora | Reader auto-scaling (2–15) on CPU/connections. Partitioning keeps indexes small. |
| DynamoDB, SNS, SQS, SES | Managed, effectively unlimited. Raise service quotas (SES sending rate) ahead of time. |
| Back-pressure | Consumer concurrency limits protect Aurora and SES while a backlog drains after an outage. |

### Availability and DR
- **Multi-AZ everywhere:** ALB, ECS tasks, Aurora (failover typically under 30s), ElastiCache, and the regional services (SQS, SNS, DynamoDB, S3) by default.
- **Region loss (active-passive):** Aurora Global Database (lag typically under 1s, managed failover), DynamoDB global tables, S3 cross-region replication, ECS scaled to minimum in the second region, and Route 53 failover. Target **RPO ≈ seconds, RTO ≈ 15–30 min**. Active-active isn't worth the complexity until a business case requires it.
- **Backups:** Aurora PITR (35 days), S3 versioning, DynamoDB PITR.

### Security
- Network: public subnets hold only the ALB; app and data subnets are private, with security groups chained ALB → services → RDS Proxy → Aurora.
- IAM task role per service, least privilege (Notifications can read its queue and write its table, nothing else).
- **Secrets Manager** for DB credentials (rotated). RDS Proxy can use IAM auth.
- KMS encryption at rest (Aurora, S3, SQS, DynamoDB); TLS in transit.

### Delivery (sketch, not IaC)
GitHub Actions builds the image → **ECR** (scan on push) → ECS deploy with **blue/green (CodeDeploy)** and automatic rollback on alarms. DB migrations run as a one-off ECS task before the new version takes traffic, and they must be backward-compatible (expand/contract).

## 3. Decision: ECS Fargate (vs. alternatives)

| Option | Verdict |
|---|---|
| **ECS on Fargate** ✅ | No servers or cluster to run. Per-service autoscaling, AZ spread and blue/green are built in. The right ops weight for 5 services. |
| EKS (Kubernetes) | More power (operators, service mesh, portability) at a much higher ops cost. Worth it with dozens of services or an existing platform team. |
| Lambda | Fits bursty consumers, but adds .NET cold starts, per-invocation DB connections (RDS Proxy mandatory) and a 15-min limit. Consumers could move to Lambda + SQS triggers later with no design change. |
| EC2 / ECS on EC2 | Cheaper at large steady load (Graviton + Savings Plans). It's a cost optimization to apply once the load profile is known. Fargate Spot is a cheaper middle step for workers. |

## 4. Decision: no AWS API Gateway (ALB + WAF instead)

The Part B "API Gateway" is a **role**. On AWS, three pieces cover it:

| Gateway job | Who does it |
|---|---|
| TLS, routing `/api/requests` → Requests, etc. | **ALB** (path-based routing) |
| Rate limiting, OWASP rules, bot/DDoS protection | **CloudFront + WAF + Shield**, at the edge, before traffic reaches the region |
| Authentication | **Cognito** issues the JWT; each service validates it (`JwtBearer` + cached JWKS) |
| Authorization (owner/assignee/admin) | Inside each service, as in Part A |

**Why not AWS API Gateway:**
- **Cost at this volume.** API Gateway bills per request (HTTP API about $1/M, REST API about $3.50/M). At thousands of calls/s that's billions of requests and thousands of dollars a month. The ALB is billed on capacity units and costs much less for the same traffic.
- **No feature we'd use.** Its strengths are API keys, usage plans, request transformation and Lambda integration. Our client is our own SPA, the backends are containers, and WAF already does rate limiting.

**Performance: API Gateway would not make this faster.**
1. **It adds a hop.** Our services sit in private subnets, so API Gateway reaches them through a VPC Link, which still targets a load balancer: CloudFront → API Gateway → VPC Link → LB → service. It goes *in front of* the ALB, not instead of it, and typically adds a few to tens of milliseconds per call.
2. **The slow part is behind the gateway.** Latency comes from the services and the DB. That's handled by indexes, keyset paging, read replicas, RDS Proxy and Redis, and API Gateway changes none of it.
3. **Its edge feature is already covered.** An "edge-optimized" API Gateway endpoint is CloudFront in front, which we already have.
4. **It caps throughput.** The default account limit is about 10k req/s per region. It can be raised, but it's one more ceiling to manage; the ALB has no request quota.
5. **Its response cache (REST APIs only) doesn't fit.** Search results depend on who is asking (owner/assignee/admin), so the cache key would have to include the user, and the hit rate would be low. Getting that key wrong would serve one user's requests to another: a security bug, not a speedup. Where caching is safe, it's already in place: CloudFront for static files, Redis for lookups shared across users.

Performance comes from the data layer and caching; the gateway choice is about cost and control.

**When to add API Gateway:** opening a **public API to third parties** (API keys, per-client quotas, usage plans, request validation), as a separate entry point in front of the same ALB. Or when moving endpoints or consumers to **Lambda**.

## 5. Left out on purpose
- IaC (Terraform/CDK): excluded by the spec.
- Multi-region active-active, the OpenSearch search projection, Aurora Limitless: described above as the next steps, with the trigger for each.
- Analytics lake (Firehose → S3 → Athena) for BI beyond the Reporting service: add it when BI needs ad-hoc queries over history.
