# Phase 6.5-0 Visual Inventory

## Overview

This document records the Phase 6.5-0 visual inventory for the Avalonia HMI UI. The audit was performed with read-only scans of current `.axaml` files. No `src` files were modified during the inventory pass.

## Current `UI.Shared` State

Path: `src\Shared\IIoT.Edge.UI.Shared\Avalonia`

Current shared UI assets before P6.5-A:

- `Styles\EdgeTheme.axaml`: basic color definitions only.
- `Views\EmptyStateView.axaml`: basic empty-state component.
- Missing at inventory time: shared `Controls` folder, card/chip/status/KPI controls, shared layout structures.

## Hardcoded Visual Styles

### Hardcoded Colors

Most migrated pages already use `DynamicResource` color references. The major outlier is Launcher:

- `src/Edge/IIoT.Edge.Launcher/Styles/LauncherTheme.axaml` owns an isolated dark palette such as `#0F1518`, `#161E22`, `#0F766E`, and `#5EEAD4`.
- Launcher visual alignment is intentionally deferred to P6.5-E; P6.5-A must not touch Launcher.

### Hardcoded CornerRadius

Current migrated pages contain many scattered `CornerRadius` values such as `6`, `8`, `14`, `18`, `27`, and `999`. P6.5-A starts this cleanup with the Equipment pilot only.

Equipment pilot baseline:

- `EquipmentView.axaml` hex colors: `0`
- `EquipmentView.axaml` `CornerRadius`: `9`
- `EquipmentView.axaml` `BorderBrush`: `7`

### Duplicated UI Structures

Common repeated structures:

- Card/panel blocks built from `Border + Padding + Background + BorderBrush + CornerRadius`.
- Status chips built from `Border + TextBlock`.
- Status dots or small state indicators built directly in page XAML.
- Section headers built manually from stacked `TextBlock` elements.

## Anti-Patterns To Avoid

- Do not create new parallel UI projects such as `IIoT.Edge.UI.Avalonia`.
- Do not copy implementation from `IIoT.EdgeClient.AvaloniaMigration`; it is only a negative reference.
- Do not move hardcoded colors and corner radii from pages into shared controls.
- Do not introduce fake production, device, log, PLC, MES, or Cloud data.
- Do not batch-rewrite all pages in P6.5-A.

## P6.5-A Start Condition

P6.5-A can start after this file exists under:

`IIoT.EdgeClient/docs/avalonia-migration/phase-6.5/visual-inventory.md`

Screenshot baseline was not completed in P6.5-0. P6.5-A may document screenshot gaps, but full screenshot acceptance is deferred to later visual sub-phases.
