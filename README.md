# financial-data-cleaner

A spec-driven platform that starts with **offline market-data quality validation**
and is designed to grow into strategy research and, eventually, a
deployment/execution pipeline.

The first feature is a command-line tool that inspects a CSV file of timestamped
OHLCV (Open, High, Low, Close, Volume) price data — typically forex — and reports
on the *quality* of that data **without altering it**. It answers one question for
a trader, quant researcher, or data engineer about to use a historical price file:
*"can I trust this data?"*

## Status

Specification phase. No implementation code yet.

| Item | Location |
| --- | --- |
| Project constitution (non-negotiable principles) | [`.specify/memory/constitution.md`](.specify/memory/constitution.md) |
| Feature specs | [`specs/`](specs/) |
| First spec — OHLCV data-quality validator | [`specs/001-ohlcv-data-quality-validator/spec.md`](specs/001-ohlcv-data-quality-validator/spec.md) |

## Core principles

Governed by the [constitution](.specify/memory/constitution.md); in short:

1. **Test-First (non-negotiable)** — a failing test precedes every unit of behavior.
2. **Framework-agnostic business logic** — Domain/Application layers held to 100% line and branch coverage.
3. **Clean (hexagonal) architecture** — Domain → Application → Infrastructure/Presentation, dependencies inward only.
4. **Deterministic, reproducible results** — same input and config, identical output, every run.
5. **Fail safe, never fail silent** — invalid or ambiguous input stops and reports rather than guessing.
6. **Observable and auditable by default** — every run emits a structured, traceable record.
7. **Simplicity now, cheap extension points** — build what the spec asks, keep boundaries clean.

## Technology standards

- **C# / .NET** (currently .NET 10) is the default for every module.
- All numeric/date/time parsing is **culture-invariant** — no dependence on host locale.
- Any value influencing a trading, reporting, or risk decision uses `decimal` — never `float`/`double`.
- Time is **normalized to UTC internally**; other offsets may be accepted and displayed, never computed against.

## Development workflow

This project is built with [Spec Kit](https://github.com/github/spec-kit). Each
module ships through the full cycle:

```
/speckit.specify → /speckit.clarify → /speckit.plan → /speckit.tasks → /speckit.implement
```

with `/speckit.checklist` and `/speckit.analyze` as recommended quality gates
before implementation begins.

Spec Kit assets live in `.specify/` (scripts, templates, constitution) and
`.clinerules/workflows/` (agent command definitions).
