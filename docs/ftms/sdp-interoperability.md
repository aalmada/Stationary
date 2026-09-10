---
type: Bluetooth service specification section
title: "5 SDP Interoperability"
description: "Service Discovery Protocol interoperability requirements."
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

# 5 SDP Interoperability

If this service is exposed over BR/EDR, then it shall have the following SDP record.

| Item                         | Definition   | Type   | Value                     | Status   |
|------------------------------|--------------|--------|---------------------------|----------|
| Service Class ID List        |              |        |                           | M        |
| Service Class #0             |              | UUID   | «Fitness Machine Service» | M        |
| Protocol Descriptor List     |              |        |                           | M        |
| Protocol #0                  |              | UUID   | L2CAP                     | M        |
| Parameter #0 for Protocol #0 | PSM          | Uint16 | PSM = ATT                 | M        |
| Protocol #1                  |              | UUID   | ATT                       | M        |
| BrowseGroupList              |              |        | PublicBrowseRoot*         | M        |

Table 5.1: SDP Record

* PublicBrowseRoot shall be present; however, other browser UUIDs may also be included in the list.

