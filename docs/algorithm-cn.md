# ReciteHelper 复习算法说明（FSRS-6）

> 本文面向希望了解 ReciteHelper 智能复习原理的用户与开发者，给出 FSRS-6 记忆模型的完整数学描述、在代码中的实现位置，以及 ReciteHelper 在其上做出的工程决策。实现代码位于 `ReciteHelper.Core/Scheduling/`，全部为纯函数，可独立测试。

## 1. 为什么从 SM-2 升级到 FSRS-6

早期版本使用的 SM-2 算法（SuperMemo-2）用一个标量"易度因子 EF"近似题目的难度，复习间隔按 `间隔 × EF` 机械递增。它有两个结构性缺陷：

1. **时间维度缺失**：SM-2 不知道"距离上次复习过了多久"，因此无法回答"这道题现在还记得的概率是多少"这个关键问题，只能按固定节奏推进。
2. **难度与稳定度耦合**：一道"很难但记住了"的题和一道"很简单"的题在 SM-2 里难以区分。

FSRS-6（Free Spaced Repetition Scheduler，第六版）把记忆状态拆成三个维度，并直接以"回忆概率"为调度目标：

| 维度 | 含义 | 取值范围 |
| --- | --- | --- |
| **S** 稳定度（Stability） | 记忆痕迹强度：回忆概率跌到 90% 所需的天数 | 0.001 – 36500 天 |
| **D** 难度（Difficulty） | 这道题对本人的固有难度 | 1.0 – 10.0 |
| **R** 可提取性（Retrievability） | 当前能回忆起来的概率 | 0 – 1 |

## 2. 遗忘曲线与调度目标

给定稳定度 S 与经过天数 t，回忆概率为：

$$
R(t, S) = \left(1 + F \cdot \frac{t}{S}\right)^{-w_{20}}, \qquad F = 0.9^{-1/w_{20}} - 1
$$

该式保证了标定性质 **R(S, S) = 0.9**：间隔恰好等于稳定度时，回忆概率恰为 90%。

调度目标由此定义：给定目标保留率（ReciteHelper 默认 **0.9**），令 R = 目标值反解出建议间隔：

$$
I(S) = \frac{S}{F}\left(R_{\text{target}}^{1/w_{20}} - 1\right)
$$

一道题"到期"，当且仅当其预测回忆概率跌破目标保留率。`ReviewGenerator` 按「到期题（回忆率升序）→ 从未做过的新题 → 其余题」的顺序组织复习会话。

实现：`FsrsAlgorithm.Retrievability` 与 `FsrsAlgorithm.IntervalForRetention`。

## 3. 状态转移

### 3.1 首次作答

$$
S_0(G) = w_{G-1}, \qquad D_0(G) = w_4 - e^{w_5 (G-1)} + 1
$$

其中 G 为本次作答等级。ReciteHelper 把二元判分结果（对/错）映射为四档 FSRS 等级（`ReviewGrader.FromJudgement`）：答对映射为 Good/Easy 区间，答错映射为 Again。

### 3.2 复习作答

难度更新（带线性阻尼与均值回归）：

$$
D' = w_7 \cdot D_0(\text{Easy}) + (1 - w_7)\left(D + \Delta D \cdot \frac{10 - D}{9}\right), \quad \Delta D = -w_6 (G - 3)
$$

稳定度更新按"成功回忆"与"遗忘"分两支：

- 成功回忆（G > Again）：

$$
S'_\text{rec} = S \cdot \left(1 + e^{w_8} (11 - D) \cdot S^{-w_9} \left(e^{w_{10}(1-R)} - 1\right) \cdot \text{hard} \cdot \text{easy}\right)
$$

- 遗忘（G = Again，取两式较小者）：

$$
S'_\text{lap} = \min\left( w_{11} D^{-w_{12}} \left((S+1)^{w_{13}} - 1\right) e^{w_{14}(1-R)},\; S / e^{w_{17} w_{18}} \right)
$$

- 同日重复作答（间隔不足一天）：

$$
S'_\text{day} = S \cdot e^{w_{17}(G - 3 + w_{18})} \cdot S^{-w_{19}}，\; G \ge \text{Hard 时下限为 1}
$$

实现：`FsrsAlgorithm.NextState` 及其内部的 `StabilityAfterRecall` / `StabilityAfterLapse` / `SameDayStability`。

### 3.3 数值健壮性

所有转移输出都经过 `Math.Clamp` 约束在合法区间（稳定度 0.001–36500 天，难度 1–10），并对非法等级、无效状态显式抛出异常。21 个参数在构造时校验范围与有限性（`SchedulerParameters.Validate`），反序列化自项目文件时同样受保护——坏数据不可能进入调度器。

## 4. 个性化拟合

FSRS-6 的 21 个权重 `w0–w20` 自带在大规模公开复习数据上拟合的群体默认值（`SchedulerParameters.Default`，约 2 万个 Anki 收藏集），新项目零数据即可使用。

ReciteHelper 在此之上实现了**逐项目个性化**（`ReviewPersonalizationService`），策略刻意保守：

| 参数 | 值 | 含义 |
| --- | --- | --- |
| `MinimumScoredReviews` | 800 | 少于 800 次有效作答不拟合——公开数据上小样本拟合只对少数人有收益 |
| `RefitGrowthFactor` | 1.25 | 作答量增长不足 25% 不重复拟合 |
| `MinimumRelativeImprovement` | 10⁻³ | 目标函数相对改善不足 0.1% 不启用新参数 |

拟合在关闭答题窗口后的后台线程执行（约一秒），流程为：从作答历史提取复习序列 → 优化器拟合（`FsrsOptimizer.Fit`）→ 留出目标检验 → 通过后**重放每道题的完整历史**重建记忆状态（`RebuildMemoryRecords`），保证存储的记忆状态与新参数自洽。

## 5. 旧数据迁移

旧版本（SM-2 时代）项目文件里没有 FSRS 记忆状态，只有带时间戳的作答记录（`ReviewTag`）。`LegacyReviewMigrator.Replay` 按时间顺序重放这些记录：旧 quality 值映射为 FSRS 等级，逐条做状态转移，重建出等价的 `MemoryRecord`。迁移是惰性的——题目第一次被调度到时才执行，且只执行一次。

`ReviewCalendar` 把自然时间转换为"学习日"：一天从凌晨 4 点开始（Anki 惯例），熬夜跨过午夜仍算同一天；时钟回拨被钳制为同日复习，不会产生负间隔。

## 6. 数据结构

每道题的 FSRS 状态只占五个数字，随 `.rhproj` 保存：

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

`algorithm` 字段为未来算法升级预留：读取时可以识别旧算法的状态并按需重放迁移。

## 7. 参考文献

1. Ye, J., Su, J., & Cao, Y. (2024). *A Stochastic Shortest Path Algorithm for Optimizing Spaced Repetition Scheduling*. KDD 2024.
2. Jarrett, A. M., et al. *FSRS: Free Spaced Repetition Scheduler*. open-spaced-repetition 项目仓库.
3. Ebbinghaus, H. (1885). *Über das Gedächtnis*（遗忘曲线的最早定量研究）.
4. Piotr Wozniak. *Application of a computer to improve the results obtained in working with the SuperMemo method*（SM-2 原始描述）.
