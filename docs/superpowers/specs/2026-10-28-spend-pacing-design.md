# Spend Pacing - Design Spec

Date: 2026-10-28
Status: Approved (design), pending implementation plan
Prototype: `.lavish/spend-pill.html` (direction P3)

## 1. Purpose

A spend-only account (spec 2026-09-14, "Accounts billed by spend only" in the README) shows
one ring today: the month's spend against its cap. That collapses the pill into a lone circle,
and it says how much is gone without saying whether the pace is sustainable.

This change splits the monthly allowance into a daily and a weekly share and shows three
rings, today, this week and the month, in the same shape as the three-ring pill of a
subscription account.

Success criteria:

- On a spend-only account the pill shows three rings: today's share, this week's share, and the
  month. Each reads as a percentage the same way the subscription rings do.
- Unspent money rolls forward: a quiet day raises every later day's share.
- The daily bars in the detail card add up to the spend the app has observed this month (section 5).
- Nothing changes for subscription accounts.

## 2. Facts this rests on

- `GET /api/oauth/usage` returns `spend.used`, `spend.limit` (minor units, `exponent`),
  `spend.severity` and `spend.percent`. It returns no reset time for the spend cap. Its
  `pace`, `extra_usage.daily` and `extra_usage.weekly` fields exist but are null for this
  account (checked 2026-10-28 with the Windows "enterprise" token).
- claude.ai, Settings > Usage, shows for the same account: "Spend limit - Resets Sat, Oct 31,
  7:00 PM CDT", and labels its daily chart "Dates in UTC". So the **cap resets at 00:00 UTC on
  the 1st**: the budget month is the UTC calendar month.
- The cap can change mid-month. It went from $100 to $1,000 on 2026-10-28 without resetting
  `used`.
- The app polls every `pollIntervalMinutes` (default 5) and keeps nothing between runs except
  `settings.json`.

## 3. Periods

All times convert with the machine's local time zone (`TimeZoneInfo.Local`), which handles DST.

- **Month**: `[1st 00:00 UTC, next 1st 00:00 UTC)`. For CDT, October runs Sep 30 19:00 to
  Oct 31 19:00 local.
- **Day**: the local calendar day, clipped to the month. A day's span is
  `[max(local midnight, month start), min(next local midnight, month end))`. So the month's
  first day can be a few hours long (Sep 30 19:00-24:00 for CDT), and so can its last day
  (Oct 31 00:00-19:00). Each counts as a day.
- **Week**: Monday 00:00 local to the next Monday, clipped to the month the same way.
- **Days left** at instant `t`: the number of days (as defined above) from the day containing
  `t` to the month's last day, both included. Oct 28 14:08 CDT gives 4: Oct 28, 29, 30 and 31.

The day boundary is local midnight, not UTC midnight, because "today" must mean the user's
day. With UTC days a CDT user's ring would reset at 7 PM. The cost is the ragged edges above,
and that the card's bars do not line up exactly with claude.ai's UTC chart.

## 4. Shares (roll forward)

`opening(t)` is the spend total at instant `t`, from the ledger (section 5). Period starts are
the clipped ones from section 3: on the month's first day and in its first week the start is the
month start itself (Sep 30 19:00 CDT), not local midnight or Monday.

- **Today's share** = `(limit - opening(day start)) / days left at day start`.
- **This week's share** = `(limit - opening(week start)) / days left at week start x days of
  this week inside the month`.
- **Month** = `limit`.
- **Spent in a period** = `used - opening(period start)`.
- **Percent** = `spent / share x 100`, not capped, so being over shows as `126`. The arc stops
  at 100.
- A share of zero or less (the cap is already reached) reads as 100%.
- The cap in force now is used for every share, so a mid-month cap change re-spreads what is
  left from that moment.

Example, cap $1,000, Wed Oct 14 at 14:00 CDT, $380 spent by Monday, $430 by midnight, $470 now:

- today: share (1000 - 430) / 18 = $31.67, spent $40, so 126%;
- week: share (1000 - 380) / 20 x 7 = $217, spent $90, so 41%;
- month: 47%.

## 5. Spend ledger

The API has no history, so the app keeps its own, at `%APPDATA%\usage-pill\spend-ledger.json`:

```json
{
  "month": "2026-10",
  "currency": "USD",
  "days": {
    "2026-10-27": { "first": 93.12, "last": 141.80 },
    "2026-10-28": { "first": 141.80, "last": 150.03 }
  }
}
```

- The keys are local dates. `first` is the first reading of that day and `last` the latest.
- Every successful poll of a spend-only account records `used` into today's entry, then saves.
  The save is atomic (temp file and move), as `settings.json` is.
- **opening(t)**: the `last` of the latest day before `t`'s day that has an entry. If there is
  none, it is 0 when `t` is the month start, and otherwise the `first` of the earliest entry at
  or after `t`. So spend made while the app was closed between two days it saw counts toward the
  later day.
- **Invariant**: the daily spends add up to `used - opening(first day)`, where the first day is
  the earliest entry's day. That is all of `used` when the ledger began on the month's first
  day. When it began later (install mid-month, or a restart after a reset), the spend before the
  first reading belongs to no day and appears in no bar.
- Keys are only unique within one ledger month. With UTC+9 the old month's last day and the new
  month's first day are both `2026-11-01`. That is safe because the ledger starts over with the
  month.
- The ledger starts over when the UTC month changes, when `used` falls below the latest recorded
  value (an early reset, or a different account), or when the currency changes.
- A missing or damaged file means an empty ledger, as for `settings.json`. The first poll after
  installing this version therefore opens today and this week at the current total, so both
  rings start at 0% instead of guessing.

## 6. Pill

On a spend-only account the pill shows three rings in this order: **today, this week, month**.
That is short to long, like Session, Weekly and per-model. The `rings` settings do not apply,
just as they do not apply to today's single spend ring.

- **Ring text**: the percentage from section 4, for example `126`.
- **Colour**: the month ring keeps the current rule (`RingGeometry.ColorFor` with the API's
  severity). The today and week rings use the ring colour below the warn threshold (default 75%),
  amber from the threshold to 100%, and red above 100%. `ColorFor` has no over-100 rule, so this
  needs a new function next to it.
- **Text slot** (`showResetTimeText`): the pill has one slot after the rings, not one per ring
  (the prototype drew one per ring). It shows today's money left, `$19`, or `+$8` when over.
  This mirrors the subscription pill, whose slot shows the time left on the first ring.
- **Tooltip**, one line per ring:
  - `Today 126% - $40 of $32`
  - `This week 41% - $90 of $217`
  - `October 47% - $470.00 / $1000`
- **Loading, errors and stale data**: the same rules as today (grey `-`, amber `!` on the first
  ring, stale dot and dimming), applied to all three rings.

## 7. Tray icon

The tray shows the pill's first ring, as it does for subscription accounts (Session), so on a
spend-only account it shows today's share. Its tooltip reads `Claude today 126% of $32`.

## 8. Detail card

For a spend-only account the "Monthly spend" row is replaced by:

- three rows with meters: `Today - $40 of $32`, `This week - $90 of $217`, and
  `October - $470.00 / $1000`, each in its ring's colour;
- a bar strip, one bar per day of the month, with the height being that day's spend. A bar is
  red when the day went over its share, and in the ring colour otherwise. Today's bar is
  outlined, future days are empty stubs, and a dashed line marks the even split
  (`limit / days in month`);
- a footer with the reset time in local time: `Resets Sat Oct 31, 7:00 PM`.

Daily spend = `last` of that day minus `opening(day start)`. Days without an entry have no bar.

## 9. Architecture

Everything new lives in one folder, `src/UsagePill/Pacing/` (namespace `UsagePill.Pacing`):

- `SpendMonth.cs`: the days of one budget month in one time zone (section 3). Pure.
- `SpendLedger.cs`: an immutable record with `Record` and `OpeningOf` (section 5). Pure.
- `SpendBudget.cs`: `SpendBudget.Compute` returns the shares, readings and daily bars of
  sections 4 and 8. Pure.
- `SpendLedgerStore.cs`: load and save. It shares the atomic write in `SettingsStore` by
  extracting that into `Settings/AtomicFile.cs`, not by copying it.
- `SpendTracker.cs`: on each new spend-only snapshot it records it into the ledger, saves, and
  computes the budget at the snapshot's capture time, so stale data shows the budget it had.

`App.Render` asks the tracker for the budget and passes it to the pill, card and tray next to
the `UsageState`. In the UI, a `RingId` enum (`Session`, `WeeklyAll`, `WeeklyScoped`,
`SpendToday`, `SpendWeek`, `SpendMonth`) replaces the nullable `LimitKind` that stands for the
spend ring today.

## 10. Testing

Unit tests, all on pure code, with injected time zones, including Central (DST ends Sun Nov 1,
2026, so the day is 25 hours) and UTC+9 (where the month ends at 09:00 local on the 1st):

- periods: month, day and week boundaries and their clipping, and days left;
- shares: roll forward, over 100%, a share of zero, and a cap change mid-month;
- ledger: the `opening` rules, month rollover, `used` decreasing, currency change, and a
  persistence round trip with a damaged file;
- the invariant from section 5, for a ledger that began on the month's first day and one that
  began mid-month.

End to end: install, run on the real account, and screenshot the pill (vertical and
horizontal), the tooltip and the detail card.

## 11. Non-goals

- A setting for the reset day. The UTC calendar month is what Anthropic does. Revisit if the
  API ever reports a reset time.
- Fixed daily shares (`limit / days in month` without rolling forward).
- Hiding individual spend rings, or a text slot per ring.
- Pacing for subscription accounts' extra credits.
