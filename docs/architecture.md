# PrintHop Architecture & Internal Flows

This document details the architectural patterns, concurrency limits, and security constraints of PrintHop. All descriptions here are based entirely on the implemented code.

## 1. Core Modules

PrintHop is broken down into the following key services:
- **`Program.cs` / `TrayAppContext.cs`**: The main entry points. They enforce a single-instance `Mutex` so only one PrintHop runs at a time. `TrayAppContext` builds the System Tray icon menu and bootstraps all the background services.
- **`UdpDiscovery.cs`**: Handles LAN Peer Discovery. It broadcasts an `AnnouncePacket` over UDP Port 4221 every few seconds. When it hears another PrintHop instance, it adds them to a local cache.
- **`HttpServer.cs`**: A lightweight, multi-threaded `HttpListener` on TCP Port 4222. It performs two roles:
  - Serves the static Vanilla HTML/JS frontend from the `www/` directory.
  - Serves the REST API (`/api/receive-print`, `/api/jobs`, etc.).
- **`PrintJobManager.cs`**: The queue buffer. It safely holds incoming print tasks and dispatches them sequentially using a background worker `Task`.
- **`PrintService.cs`**: The execution layer. It relies on `System.Drawing.Printing` and GDI+ to rasterize images and send them to the Windows print spooler.
- **`ActivityMonitorService.cs`**: Persists the device whitelist (`devices.json`) and the rolling audit log (`activity_logs.json`).
- **`FirewallService.cs`**: Uses the Windows `netsh advfirewall` CLI tool to automatically open Ports 4221 and 4222. (Requires UAC Administrator privileges during the first launch).

## 2. Print Job Lifecycle & Concurrency

To ensure PrintHop operates flawlessly on legacy machines restricted to **2GB of System RAM**, the job ingestion and execution phases are strictly decoupled.

### A. Ingestion Phase
When a client sends a document, they use a raw binary HTTP POST to `/api/receive-print`. Metadata is passed via `X-PrintHop-*` HTTP headers.
- **Upload Concurrency**: Capped at **3** simultaneous streams using a `SemaphoreSlim`.
- **Memory Footprint**: The `HttpServer` reads the incoming request stream directly into a local temporary file using a fixed **80KB buffer**.
- **Result**: The job is assigned a GUID, added to `PrintJobManager`, and the client receives a 200 OK ("Queued") response.

### B. Execution Phase
The `PrintJobManager` continuously watches the queue.
- **Execution Concurrency**: Strictly **1**. The manager worker thread dequeues and processes a single job at a time.
- **Memory Footprint (Rasterization)**: *Theoretical Constraint.* When `PrintService` executes a job, GDI+ unpacks the image (e.g., a JPEG) into uncompressed bitmap RAM. Depending on the size of the document, this can spike memory consumption into the hundreds of megabytes. By forcing execution to happen sequentially, we prevent multiple GDI+ spikes from overlapping, keeping the ceiling under the 2GB OS limit.

### C. Job States
Jobs move through three states:
1. `Queued`: Residing on disk, waiting for the background worker.
2. `Printing`: Currently being rasterized and sent to the spooler.
3. `Success` / `Failed` / `Blocked`: The final disposition. The temporary file is deleted, and a webhook is dispatched to the sender.

## 3. Webhook Notification System

Because ingestion and execution are asynchronous, the sender needs to be notified if their queued job fails or is manually blocked by the host.

### The Callback Flow
1. When a job completes, `PrintJobManager` extracts the `SenderIp` from the job. (This IP was captured directly from the raw TCP socket during upload, preventing header spoofing).
2. The Host validates that the `SenderId` is still approved.
3. The Host fires an HTTP POST payload containing the `jobId` GUID to `http://<SenderIp>:4222/api/activity-logs/remote`.

### Sender-Side Validation
When the sender receives the webhook:
1. It validates that the `jobId` exists in its internal cache of recently dispatched jobs (`_dispatchedJobs`).
2. If it matches, the status update is accepted into the local Activity Log.

### Known Limitations / Threat Model
- **No Cryptographic Transport Security**: PrintHop communicates over standard HTTP. There is no mTLS (Mutual TLS).
- **Residual Risk**: Because there is no encrypted tunnel, an active MITM (Man-in-the-Middle) attacker on the LAN could theoretically sniff the `jobId` GUID and manually spoof a webhook payload. We accept this residual risk given the intended "trusted small-business LAN" threat model.
