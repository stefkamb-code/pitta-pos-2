# Πίττα του Παππού — POS

Windows desktop POS (point-of-sale) application for the grill shop **«Πίττα του Παππού»**.
Built from the interactive design mockups in [`design/`](design/) — open
`design/POS Order Screen.dc.html` and `design/Live Orders Board.dc.html` in a browser to see
the intended UI and behaviour (they are fully clickable prototypes).

Ελληνική σύνοψη προδιαγραφών: [docs/features-el.md](docs/features-el.md).

## Screens

### 1. POS Order Screen (5-step wizard)

Fixed 1280×800 layout, Greek UI, with a step bar: **1 ΤΥΠΟΣ → 2 ΠΕΛΑΤΗΣ → 3 ΠΡΟΪΟΝΤΑ → 4 ΔΙΑΝΟΜΗ → 5 ΕΚΤΥΠΩΣΗ**. Completed steps stay clickable for going back.

**Step 1 — Order type.** Four options: ΔΙΑΝΟΜΗ (own delivery driver), ΕΦΑΡΜΟΓΕΣ (e-food / Wolt / BOX), ΠΑΡΑΛΑΒΗ (pickup at the shop), ΤΡΑΠΕΖΙ (dine-in). Choosing ΤΡΑΠΕΖΙ shows a grid of table numbers (1–12). Step routing depends on the type:

| Type | Flow |
|---|---|
| ΔΙΑΝΟΜΗ | 1 → 2 (customer + address) → 3 → 5 (also pushes order to the Live Orders board) |
| ΕΦΑΡΜΟΓΕΣ | 1 → 2 → 3 → 4 (choose e-food / Wolt / BOX) → 5 (pushes to board) |
| ΠΑΡΑΛΑΒΗ | 1 → 3 (skips customer) → 4 (pickup time: Άμεσα / 15' / 30' / 45') → 5 |
| ΤΡΑΠΕΖΙ | 1 (+ table number) → 3 → 5 |

**Step 2 — Customer.** Search existing customers by phone or name (live matches with one-tap select), or type new details: name, phone, and address (address only for delivery/apps orders). Continue requires at least a name.

**Step 3 — Products.** Three-pane layout:
- Left: category rail (ΣΟΥΒΛΑΚΙΑ, ΠΙΤΤΕΣ, ΜΕΡΙΔΕΣ, BURGERS, ΣΑΛΑΤΕΣ, ΟΡΕΚΤΙΚΑ, ΑΝΑΨΥΚΤΙΚΑ).
- Middle: product tile grid. Tapping a tile adds it to the order (tapping again increments the quantity; a quantity badge on the tile decrements). Optional secondary English names on tiles.
- Right: the live order ticket — per-line quantity stepper, edit (✎) and remove (×), per-line description of customizations, subtotal, item discounts, a whole-order discount (% stepper + editable field), and the total.

**Customizer** (for customizable categories, e.g. ΠΙΤΤΕΣ): opens in the middle pane with
- bread choice (Ελληνική / Αραβική / Ψωμί) as a segmented control,
- included ingredients (Ντομάτα, Μίγμα, Κρεμμύδι, Αλάτι, Πιπέρι, Κίτρινη σάλτσα) shown as chips — tap to remove/restore, plus a ΣΚΕΤΟ toggle that removes all,
- extras with per-extra prices and quantity (max ×2), searchable; free extras show «δωρεάν»,
- per-item note («Παρατηρήσεις»), «Χωρίς χρέωση» (no-charge) toggle, per-item discount stepper (0–50 %, step 5),
- quantity stepper and a live-priced ΠΡΟΣΘΗΚΗ button.
Cart lines created this way can be re-opened and edited from the ticket.

**Step 4 — Dispatch.** Apps orders: pick the platform (e-food red, Wolt blue, BOX gold). Pickup orders: pick the pickup time.

**Step 5 — Print.** Receipt preview (shop name, order number, type, customer/table, lines, total), ΕΚΤΥΠΩΣΗ (print) button, and «Νέα παραγγελία» which resets the wizard and increments the order number.

Header (always visible): shop logo, current order context (type · customer · address), a ΠΑΡΑΓΓΕΛΙΕΣ button with a pending-count badge linking to the Live Orders board, EL/EN language toggle, and a clock.

### 2. Live Orders Board (ΖΩΝΤΑΝΕΣ ΠΑΡΑΓΓΕΛΙΕΣ)

Dispatcher view for delivery/apps orders, shared state with the POS screen (in the prototype via `localStorage`; in the real app this must be shared application state / a database).

- Left: pending orders list — order number, type/channel, customer name & address, total, and a live elapsed timer that colour-codes age (green < 10 min, amber ≥ 10, red ≥ 20).
- Right: select an order → «ΠΕΡΑΣΕ ΤΗΝ ΣΕ» dispatch buttons: Ίδιος διανομέας, e-food, Wolt, BOX, Κάρτα Μαγαζιού.
- Bottom-right: per-channel stat tiles (count · sum €). Tapping a channel lists its dispatched orders, each reassignable to another channel or revertible back to pending («↩ Αναμονή»).

## Design system

`design/_ds/modernist-*/` contains the "Modernist" design system: flat and architectural, **zero corner radius**, strong 2 px dividers, Archivo font, light ground `#f3f2f2`, ink `#201e1d`, single red accent `#ec3013`, flush-left labels. See its `readme.md` and `styles.css` for tokens. Prices are formatted `€3,20` (comma decimals).

## Tech (decided)

- C# / .NET 8, WPF for the desktop UI, MVVM.
- **Single machine**: the POS wizard and the Live Orders board run in the same app (board as a second window/view) sharing one local SQLite database — no network sync needed.
- SQLite for local data (menu, customers, orders).
- **Thermal receipt printer** (ESC/POS) for receipts — exact model/connection TBD.

Building requires the **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`).

## Open questions

- Thermal printer model & connection (USB/network); fiscal requirements (ΑΑΔΕ/myDATA) or plain slips?
- Menu management UI (edit products/prices in-app) or config file?
- Are e-food/Wolt/BOX orders entered manually, or is any API integration wanted?
