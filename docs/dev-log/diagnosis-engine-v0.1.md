# Diagnosis Engine v0.1 Development Log

## Goal

Add the first evidence-based incident analysis layer.

The engine does not claim a perfect root cause. It converts collected evidence into ranked hypotheses with confidence and explanations.

## Design

Input:

- unified incident timeline
- telemetry samples
- Windows event evidence
- process and foreground events
- heartbeat information

Output:

- suspected category
- confidence score
- supporting evidence
- missing evidence

## Initial categories

### GPU Driver Timeout

Signals:

- Display/TDR events
- GPU activity before failure
- graphics application foreground state
- missing WHEA evidence

### Unexpected Power Loss

Signals:

- Kernel-Power 41
- EventLog 6008
- missing clean shutdown marker

### Hardware Instability

Signals:

- WHEA events
- machine check errors
- CPU/memory related evidence

### Application Crash

Signals:

- application crash events
- process termination near failure window

## Implementation Plan

Phase 1:

- IncidentAnalysisResult model
- rule based evidence scorer
- deterministic classification

Phase 2:

- correlation engine
- timeline pattern matching
- evidence weighting

Phase 3:

- report generation
- exportable diagnosis summary

## Validation Points

### TC001 Normal shutdown

Expected:

- no false incident diagnosis
- clean shutdown remains true

### TC002 Forced Agent termination

Expected:

- incident generated on next startup
- analysis consumes collected evidence

### TC003 GPU driver reset

Expected:

- Display/TDR evidence increases GPU timeout confidence

### TC004 Unexpected reboot

Expected:

- Kernel-Power 41 and EventLog 6008 detected

### TC005 WHEA hardware event

Expected:

- hardware instability category considered

## Current Limitation

The first version is evidence ranking, not a replacement for vendor diagnostics or hardware testing.
