# ReciteHelper Review Algorithm Notes (FSRS-6)

> This document explains how the smart-review feature works: the full FSRS-6 model, where each piece lives in the codebase, and the engineering decisions ReciteHelper makes on top of it. All scheduling math lives in `ReciteHelper.Core/Scheduling/` as pure, unit-testable functions.

## 1. Why FSRS-6 replaces SM-2

Early versions used the SM-2 algorithm, which approximates each question's difficulty with a single ease factor and grows intervals mechanically (`interval × EF`). It has two structural weaknesses:

1. **No notion of time**: SM-2 cannot answer "what is the probability the learner still remembers this right now?", which is exactly the quantity a scheduler should optimise.
2. **Difficulty and stability are entangled**: a hard item that was barely recalled and an easy item look the same.

FSRS-6 splits memory into three dimensions and schedules against recall probability directly:

| Dimension | Meaning | Range |
| --- | --- | --- |
| **S** Stability | Strength of the memory trace: days for recall to fall to 90% | 0.001 – 36500 days |
| **D** Difficulty | Intrinsic difficulty of the item for this learner | 1.0 – 10.0 |
| **R** Retrievability | Probability of recalling it right now | 0 – 1 |

## 2. Forgetting curve and the scheduling target

Given stability S and elapsed days t:

$$
R(t, S) = \left(1 + F \cdot \frac{t}{S}\right)^{-w_{20}}, \qquad F = 0.9^{-1/w_{20}} - 1
$$

The calibration property **R(S, S) = 0.9** holds by construction. Inverting the curve at the target retention (default **0.9**) yields the recommended interval:

$$
I(S) = \frac{S}{F}\left(R_{\text{target}}^{1/w_{20}} - 1\right)
$$

A question is *due* when its predicted recall falls below the target retention. `ReviewGenerator` orders each session as: due questions (lowest recall first) → never-seen questions → the rest.

Implementation: `FsrsAlgorithm.Retrievability`, `FsrsAlgorithm.IntervalForRetention`.

## 3. State transitions

### 3.1 First exposure

$$
S_0(G) = w_{G-1}, \qquad D_0(G) = w_4 - e^{w_5 (G-1)} + 1
$$

ReciteHelper maps the binary grading result to four FSRS grades (`ReviewGrader.FromJudgement`): correct answers land in the Good/Easy range, lapses in Again.

### 3.2 Subsequent reviews

Difficulty update (linear damping + mean reversion):

$$
D' = w_7 \cdot D_0(\text{Easy}) + (1 - w_7)\left(D + \Delta D \cdot \frac{10 - D}{9}\right), \quad \Delta D = -w_6 (G - 3)
$$

Stability update, split by outcome:

- Successful recall (G > Again):

$$
S'_\text{rec} = S \cdot \left(1 + e^{w_8} (11 - D) \cdot S^{-w_9} \left(e^{w_{10}(1-R)} - 1\right) \cdot \text{hard} \cdot \text{easy}\right)
$$

- Lapse (G = Again, clamped by the post-lapse ceiling):

$$
S'_\text{lap} = \min\left( w_{11} D^{-w_{12}} \left((S+1)^{w_{13}} - 1\right) e^{w_{14}(1-R)},\; S / e^{w_{17} w_{18}} \right)
$$

- Same-day repeats (elapsed less than one day):

$$
S'_\text{day} = S \cdot e^{w_{17}(G - 3 + w_{18})} \cdot S^{-w_{19}}, \text{ floored at 1 for } G \ge \text{Hard}
$$

Implementation: `FsrsAlgorithm.NextState` with `StabilityAfterRecall` / `StabilityAfterLapse` / `SameDayStability`.

### 3.3 Numerical robustness

Every transition output is clamped to admissible ranges (stability 0.001–36500 days, difficulty 1–10); invalid grades and states throw explicitly. The 21 weights are range- and finiteness-validated on construction (`SchedulerParameters.Validate`) and on deserialisation from project files — malformed data cannot reach the scheduler.

## 4. Personalisation

FSRS-6 ships population defaults for `w0–w20` (fitted on ~20k public Anki collections, `SchedulerParameters.Default`), so a fresh project works with zero data.

ReciteHelper adds conservative per-project personalisation (`ReviewPersonalizationService`):

| Parameter | Value | Meaning |
| --- | --- | --- |
| `MinimumScoredReviews` | 800 | Do not fit below 800 graded reviews — small-sample fits helped only a minority on public data |
| `RefitGrowthFactor` | 1.25 | Skip refitting until history grows by 25% |
| `MinimumRelativeImprovement` | 10⁻³ | Adopt new parameters only on a ≥0.1% relative objective improvement |

The fit runs on a background thread after the quiz window closes (about a second): extract review sequences from history → optimise (`FsrsOptimizer.Fit`) → check the held-out objective → on success **replay every question's full history** (`RebuildMemoryRecords`) so stored memory states are consistent with the new parameters.

## 5. Legacy data migration

Projects created by SM-2-era versions carry timestamped review tags instead of FSRS state. `LegacyReviewMigrator.Replay` replays them in chronological order: legacy quality values map to FSRS grades, and a state transition runs per tag, rebuilding an equivalent `MemoryRecord`. Migration is lazy — it runs once, the first time a question is scheduled.

`ReviewCalendar` converts wall-clock time into "study days": a day starts at 04:00 (Anki convention), so late-night sessions crossing midnight count as the same day, and clock adjustments are clamped so intervals can never go negative.

## 6. Data structure

A question's FSRS state is five numbers stored inside the `.rhproj`:

```json
{
  "memory": {
    "stability": 3.42,
    "difficulty": 5.17,
    "last_review": "2026-11-06T21:30:00",
    "reps": 4,
    "lapses": 1,
    "algorithm": "FSRS-6"
  }
}
```

The `algorithm` field is reserved for future migrations: a reader can detect state written by another algorithm version and replay it.

## 7. References

1. Ye, J., Su, J., & Cao, Y. (2024). *A Stochastic Shortest Path Algorithm for Optimizing Spaced Repetition Scheduling*. KDD 2024.
2. open-spaced-repetition project — FSRS reference implementation and parameter defaults.
3. Ebbinghaus, H. (1885). *Über das Gedächtnis*.
4. Piotr Wozniak. *Application of a computer to improve the results obtained in working with the SuperMemo method* (original SM-2 description).
