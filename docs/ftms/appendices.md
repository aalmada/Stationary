---
type: Bluetooth service specification section
title: "Appendices"
description: "Data-record transmission, control-point sequence charts, and spin-down procedure examples."
tags: [bluetooth, ftms, fitness-machine-service]
status: stable
generated:
  by: docling
  at: 2026-09-06T21:49:29Z
sources:
  - id: ftms-v1-0-1
    resource: https://www.bluetooth.com/specifications/specs/fitness-machine-service-1-0-1/
    title: Fitness Machine Service Bluetooth Service Specification v1.0.1
---

<!-- markdownlint-disable MD001 MD012 MD013 MD024 MD025 -->

# Appendices

## Appendix 1 Transmission of a Data Record

## A.1.1 Sending Data Record in a Single Notification

When a Data Record fits into a single notification, the More Data bit of the Flags field is set to 0, and the related fields are also present.

## A.1.2 Sending Data Record Split into Multiple Notifications

When a Data Record does not fit in a single ATT\_MTU payload, it is split into multiple notifications. The content of the notifications is as follows:

First notification:

- More Data bit of the Flags field is set to 1
- Some fields are present
- Fields related to the More Data bit are not present

Next notifications (if any):

- More Data bit of the Flags field is set to 1
- Some other fields are present
- Fields related to the More Data bit are not present

Last notification:

- More Data bit of the Flags field is set to 0
- Remaining fields are present
- Fields related to the More Data bit are present

## Appendix 2 Fitness Machine Control Point Message Sequence Charts

## A.2.1 Control Procedure

The message sequence chart described below applies to all Fitness Machine Control Point procedures defined in Section 4.16. Note that the Request Control Procedure was executed prior to initiating the procedure described below.

```mermaid
sequenceDiagram
    participant Client
    participant Server
    Client->>Server: Write Request: Fitness Machine Control Point<br/>Op Code: Set Target Speed<br/>Parameter Value: 10 km/h
    Server-->>Client: Write Response
    Server->>Client: Handle Value Indication<br/>Op Code: Response Code<br/>Request Op Code: Set Target Speed<br/>Response Value: Success<br/>Response Parameter: None
    Client->>Server: Handle Value Confirmation
```

## A.2.2 Error Handling - Procedure Already In Progress

The message sequence chart described below results in a bad behavior of the Client. However, this mechanism is implemented by a Server in order to handle this situation. Note that the Request Control Procedure was executed prior to initiating the procedure described below.

```mermaid
sequenceDiagram
    participant Client
    participant Server
    Client->>Server: Write Request: Fitness Machine Control Point<br/>Op Code: Set Target Speed<br/>Parameter Value: 10 km/h
    Server-->>Client: Write Response
    Client->>Server: Write Request: Fitness Machine Control Point<br/>Op Code: Set Target Incline<br/>Parameter Value: 2%
    Server-->>Client: Error Response: Procedure Already In Progress
    Server->>Client: Handle Value Indication<br/>Op Code: Response Code<br/>Request Op Code: Set Target Speed<br/>Response Value: Success<br/>Response Parameter: None
    Client->>Server: Handle Value Confirmation
```

This request is discarded by the Server.

## Appendix 3 Spin Down Procedure Examples

## A.3.1 Spin Down Procedure - Requested by the Server

The message sequence chart described below applies to the Spin Down Control procedures defined in Section 4.16.2.20. Note that the Request Control Procedure was executed prior to initiating the procedure described below.

```mermaid
sequenceDiagram
    participant Client
    participant Server
    opt Server implementation supports request notification
        Server->>Client: FTM Status<br/>Op Code: Spin-Down Requested<br/>Parameter Value: N/A
    end
    Client->>Server: Write Request: Fitness Machine Control Point<br/>Op Code: Spin-Down Control<br/>Parameter Value: START
    Server-->>Client: Write Response
    Server->>Client: Handle Value Indication<br/>Op Code: Response Code<br/>Request Op Code: Spin-Down Control<br/>Response Value: Success<br/>Response Parameters: Target Speed High, Target Speed Low
    Client->>Server: Handle Value Confirmation
    Server->>Client: Standard Indoor Bike Data including speed
    Server->>Client: FTM Status<br/>Op Code: Spin-Down Stop Pedaling<br/>Parameter Value: None
    Server->>Client: FTM Status<br/>Op Code: Spin-Down Success<br/>Parameter Value: Spin-Down Time in ms
```

## A.3.2 Spin Down Procedure - Initiated by the Client with Error

The message sequence chart described below applies to the Spin Down Control procedures defined in Section 4.16.2.20. Note that the Request Control Procedure was executed prior to initiating the procedure described below.

```mermaid
sequenceDiagram
    participant Client
    participant Server
    Client->>Server: Write Request: Fitness Machine Control Point<br/>Op Code: Spin-Down Control<br/>Parameter Value: START
    Server-->>Client: Write Response
    Server->>Client: Handle Value Indication<br/>Op Code: Response Code<br/>Request Op Code: Spin-Down Control<br/>Response Value: Success<br/>Response Parameters: Target Speed High, Target Speed Low
    Client->>Server: Handle Value Confirmation
    Server->>Client: Standard Indoor Bike Data including speed
    Server->>Client: FTM Status<br/>Op Code: Spin-Down Fail<br/>Parameter Value: None
    Note right of Server: The server returns to normal operation.<br/>The client may restart the procedure.
```

## A.3.3 Spin Down Procedure - Ignored by the Client

The message sequence chart described below applies to the Spin Down Control procedures defined in Section 4.16.2.20. Note that the Request Control Procedure was executed prior to initiating the procedure described below.

```mermaid
sequenceDiagram
    participant Client
    participant Server
    opt Server implementation supports request notification
        Server->>Client: FTM Status<br/>Op Code: Spin-Down Requested<br/>Parameter Value: N/A
    end
    Client->>Server: Write Request: Fitness Machine Control Point<br/>Op Code: Spin-Down Control<br/>Parameter Value: IGNORE
    Server-->>Client: Write Response
    Server->>Client: Handle Value Indication<br/>Op Code: Response Code<br/>Request Op Code: Spin-Down Control<br/>Response Value: Success<br/>Response Parameters: None
    Client->>Server: Handle Value Confirmation
    Note right of Server: The sequence ends. The server may send<br/>another FTM Status message to start again.
```
