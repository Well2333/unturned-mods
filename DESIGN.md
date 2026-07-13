# WebPanel Design System

This file is required reading before changing either WebPanel SPA or any plugin-owned Web UI.
It follows the DESIGN.md practice documented by VoltAgent's awesome-design-md, but defines this
project's own visual language.

## Product character

WebPanel is a compact game-server control surface: dark, calm, information-dense and operational.
It should feel closer to a polished desktop utility than a promotional website. Decoration never
competes with status, inventory, prices or actions.

## Tokens

- Canvas #0f1115; primary surface #171a20; raised/control surface #1d2129.
- Hairline border #2a303a; primary text #f4f6f8; secondary text #929aa7.
- Accent #6d72f6; success #35b36f; destructive #ef5b61.
- Spacing is based on 4px: use 4, 8, 12, 16, 24. Avoid arbitrary large gaps.
- Radius: 7px controls, 9–12px cards, pills only for tags and compact filters.
- Shadows are rare. Prefer a one-pixel border and surface contrast.

## Typography and layout

- Use the system UI stack; the in-game browser must not download fonts.
- Page titles 20px/700; card titles 13–15px/650; body 13–14px; metadata 11–12px.
- Default content is a responsive grid, not one oversized card per row.
- Dense inventory and shop catalogs target six compact columns at normal desktop widths, then
  progressively reduce through five/four/three/two/one columns as the Steam webview narrows.
- Settings use two balanced columns and collapse to one.
- Put the most frequent action beside its object. Do not create a full-width action-only panel.

## Components

- Primary form buttons are 34–40px high; dense catalog-card actions may use 27–32px controls when
  their labels remain legible. Accent means primary, green means a successful transaction, red is
  reserved for destructive or irreversible operations.
- Cards expose title, useful state, then actions. Use a compact row or tile for simple actions.
- Tags are short, read-only metadata. Never pack unrelated values into one long string.
- Forms show labels above controls and keep destructive actions visually separate.
- Empty states explain what is absent without occupying most of the viewport.

## Interaction and refresh

- Both panels refresh safe data every five seconds by default. Pause while hidden, while a modal is
  open, or while an input is active; never overwrite unsaved edits.
- Keep selected tabs and expanded context across refreshes.
- Every irreversible bulk action requires confirmation. Disable its button during the request.
- Retain manual refresh as an immediate escape hatch.

## Extension architecture

- Simple plugins use the legacy descriptor renderer.
- Complex plugins embed their own HTML, CSS and JavaScript and expose WebUiExtension; the host mounts
  it in Shadow DOM and provides capability-scoped action helpers.
- Plugin UI owns business-specific layout. WebPanel owns authentication, navigation, localization,
  refresh scheduling, error containment and theme primitives.
- The host must never branch on a concrete plugin ID.

## Guardrails

- Do not return to oversized pills, full-width single-action cards or arbitrary emoji decoration.
- Do not hard-code fixed desktop widths; the Steam overlay can be narrow.
- Do not load third-party frontend frameworks or remote assets.
- Do not use color as the only signal; retain labels and disabled/loading states.
