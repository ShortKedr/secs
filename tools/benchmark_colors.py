"""One fixed effect scale for comparable benchmark tables and charts.

Keep the printed measurement independent from its unrounded comparison value.
Legacy SpaceBattle imports this module through space-battle/heatmap.py.
"""

from dataclasses import dataclass
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
import json
from statistics import median
import xml.etree.ElementTree as ET


PALETTE = (
    ("best", "#2e9b50"), ("strong", "#59b35d"), ("good", "#91cb69"),
    ("fair", "#c7df78"), ("middle", "#f2d85c"), ("weak", "#efad46"),
    ("poor", "#e87c3f"), ("bad", "#dc513c"), ("worst", "#c93637"),
)
EQUAL_COLOR = "#8b949e"
LABELS = {
    "best": "Отставание до 5%", "worst": "Отставание от 30%",
    "relative": "Отставание между 5% и 30%", "equal": "Практически неразличимо или недостаточно данных о разбросе",
}
EN_LABELS = {
    "best": "Lag up to 5%", "worst": "Lag from 30%",
    "relative": "Lag between 5% and 30%", "equal": "Neutral or insufficient separation",
}
PROFILES = ("low", "nominal", "high")
STAGES = "collect ai thermal-inventory steering weapons carriers physics hits lifecycle".split()
TIMING_METRICS = ("wall_seconds", "tick.mean_ms", "tick.p95_ms", "tick.p99_ms", "tick.max_ms",
                  "phase.early.mean_ms", "phase.middle.mean_ms", "phase.late.mean_ms",
                  *(f"stage.{stage}.mean_ms" for stage in STAGES))


@dataclass(frozen=True)
class IndependentRun:
    run_id: str
    value: object


@dataclass(frozen=True)
class Metric:
    value: object
    suffix: str = ""
    comparison: object = None
    runs: tuple = ()
    interval: tuple | None = None

    def __str__(self):
        return str(self.value) + self.suffix


def numeric(value):
    try:
        if isinstance(value, Metric):
            value = value.value if value.comparison is None else value.comparison
        result = Decimal(str(value))
        return result if result.is_finite() and result >= 0 else None
    except (InvalidOperation, ValueError, TypeError):
        return None


class TimingPolicy:
    def __init__(self, entries=()):
        self.entries = tuple(entries)
        self.thresholds = {}
        self.phrases = {}
        for entry in self.entries:
            if not isinstance(entry, dict) or set(entry) != {"profile", "metric", "threshold_ms", "reason_en", "reason_ru"}:
                raise ValueError("Timing threshold requires exact scope, threshold_ms and bilingual reasons")
            key = (entry["profile"], entry["metric"])
            threshold = numeric(entry["threshold_ms"])
            if key[0] not in PROFILES or key[1] not in TIMING_METRICS:
                raise ValueError("Unknown timing threshold profile or metric")
            if key in self.thresholds:
                raise ValueError("Duplicate timing threshold scope")
            if threshold is None or any(not isinstance(entry[k], str) or not entry[k].strip()
                                        for k in ("reason_en", "reason_ru")):
                raise ValueError("Timing threshold must be finite/nonnegative with documented reasons")
            self.thresholds[key] = threshold
            self.phrases[entry["reason_ru"]] = entry["reason_en"]

    @classmethod
    def from_file(cls, path):
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(data, dict) or set(data) != {"timing_absolute_thresholds"} or not isinstance(data["timing_absolute_thresholds"], list):
            raise ValueError("Invalid timing threshold policy")
        return cls(data["timing_absolute_thresholds"])

    def threshold(self, profile, metric):
        value = self.thresholds.get((profile, metric))
        return value / 1000 if value is not None and metric == "wall_seconds" else value


def relative_lag(value, best, direction):
    if best == 0:
        return Decimal(0) if value == 0 else Decimal("Infinity")
    return max(Decimal(0), (value / best - 1 if direction == "lower" else 1 - value / best) * 100)


def palette_level(lag):
    if lag <= 5:
        return 0
    if lag >= 30:
        return len(PALETTE) - 1
    position = (lag - 5) * 4 / 10 if lag <= 15 else 4 + (lag - 15) * 4 / 15
    # The first shade is labelled "up to 5%". Quantising the transition must
    # never put a value beyond that boundary back into the first shade.
    return max(1, min(len(PALETTE) - 2, int(position.to_integral_value(rounding=ROUND_HALF_UP))))


def estimate(value):
    point = numeric(value)
    runs = value.runs if isinstance(value, Metric) else ()
    interval = value.interval if isinstance(value, Metric) else None
    if interval is not None:
        if runs or len(interval) != 2:
            raise ValueError("Supply either real run summaries or one reported interval")
        low, high = (numeric(v) for v in interval)
        if point is None or low is None or high is None or not low <= point <= high:
            raise ValueError("Reported interval must contain the explicit comparison value")
        return point, (low, high), True
    if not runs:
        return point, None, False
    ids = [run.run_id for run in runs]
    numbers = [numeric(run.value) for run in runs]
    if any(not isinstance(run_id, str) or not run_id.strip() for run_id in ids) or len(set(ids)) != len(ids):
        raise ValueError("Independent runs require unique nonempty run IDs")
    if any(number is None for number in numbers):
        raise ValueError("Independent run summaries must be finite nonnegative values")
    return median(numbers), (min(numbers), max(numbers)) if len(numbers) >= 2 else None, True


def marker(name, label, asset_path="heatmap"):
    return f'![{label}]({asset_path}/{name}.svg "{label}")'


def levels(values, direction="lower", absolute_threshold=None):
    """Classify a comparable group; None means unknown or not comparable."""
    if direction not in ("lower", "higher"):
        raise ValueError("Heatmap direction must be lower or higher")
    threshold = Decimal(0) if absolute_threshold is None else numeric(absolute_threshold)
    if threshold is None:
        raise ValueError("Absolute threshold must be finite and nonnegative")
    estimates = [estimate(v) for v in values]
    numbers = [point for point, _, _ in estimates]
    known = [n for n in numbers if n is not None]
    if len(known) < 2:
        return [None] * len(values)
    reference = min(known) if direction == "lower" else max(known)
    reference_index = numbers.index(reference)
    reference_range = estimates[reference_index][1]
    any_runs = any(supplied for point, _, supplied in estimates if point is not None)
    partial_runs = any_runs and any(spread is None for point, spread, _ in estimates if point is not None)
    if any_runs and not partial_runs:
        tied_ranges = [spread for point, spread, _ in estimates if point == reference]
        reference_range = (min(spread[0] for spread in tied_ranges), max(spread[1] for spread in tied_ranges))
    lags = [None if point is None else relative_lag(point, reference, direction) for point in numbers]
    meaningful = []
    for (point, spread, _), lag in zip(estimates, lags):
        distinct = lag is not None and lag > 5 and abs(point - reference) > threshold
        if distinct and any_runs and not partial_runs:
            # Envelopes describe independent run summaries, not a confidence interval.
            boundary, baseline = ((spread[0], reference_range[1]) if direction == "lower"
                                  else (spread[1], reference_range[0]))
            separation = boundary - baseline if direction == "lower" else baseline - boundary
            distinct = separation > threshold and relative_lag(boundary, baseline, direction) > 5
        meaningful.append(distinct and not partial_runs)
    neutral_group = not any(meaningful)
    output = []
    for original, number, lag, distinct in zip(values, numbers, lags, meaningful):
        if number is None:
            output.append(None)
            continue
        if neutral_group or (lag > 5 and not distinct):
            name = "equal"
        else:
            name = PALETTE[palette_level(lag)][0]
        output.append(name)
    return output


def shade_values(values, direction="lower", absolute_threshold=None, *, language="ru", asset_path="heatmap"):
    values = list(values)
    labels = EN_LABELS if language == "en" else LABELS
    if language not in ("en", "ru"):
        raise ValueError("Colour scale language must be en or ru")
    result = []
    for original, name in zip(values, levels(values, direction, absolute_threshold)):
        if name is None:
            result.append(original)
        else:
            label = labels[name if name in ("best", "worst", "equal") else "relative"]
            result.append(marker(name, label, asset_path) + " " + str(original))
    return result


def colors(values, direction="lower", absolute_threshold=None):
    palette = dict(PALETTE)
    palette["equal"] = EQUAL_COLOR
    return [palette.get(name, EQUAL_COLOR) for name in levels(list(values), direction, absolute_threshold)]


def shade_columns(rows, columns, group_by=(), absolute_thresholds=None, *, language="ru", asset_path="heatmap"):
    result = [list(row) for row in rows]
    groups = {}
    for index, row in enumerate(result):
        key = tuple(row[column] for column in group_by)
        groups.setdefault(key, []).append(index)
    for indexes in groups.values():
        for column in columns:
            threshold = absolute_thresholds(result[indexes[0]], column) if absolute_thresholds else None
            shaded = shade_values([result[i][column] for i in indexes], absolute_threshold=threshold,
                                  language=language, asset_path=asset_path)
            for index, value in zip(indexes, shaded):
                result[index][column] = value
    return result


def shade_rows(rows, columns, absolute_thresholds=None, *, language="ru", asset_path="heatmap"):
    result = [list(row) for row in rows]
    for row in result:
        threshold = absolute_thresholds(row) if absolute_thresholds else None
        for column, value in zip(columns, shade_values([row[c] for c in columns], absolute_threshold=threshold,
                                                      language=language, asset_path=asset_path)):
            row[column] = value
    return result


def compact_legend(language="en", asset_path="heatmap", details_link=None):
    if language not in ("en", "ru"):
        raise ValueError("Colour scale language must be en or ru")
    labels = EN_LABELS if language == "en" else LABELS
    examples = [marker(name, labels[key], asset_path) for name, key in
                (("best", "best"), ("middle", "relative"), ("worst", "worst"), ("equal", "equal"))]
    if language == "ru":
        lines = ["### Цветовая шкала", "",
                 f"{examples[0]} до 5% · {examples[1]} 15% · {examples[2]} от 30% · {examples[3]} нейтрально.", "",
                 "Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. "
                 "Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость."]
        if details_link:
            lines[-1] += f" [Правила сравнения]({details_link})."
    else:
        lines = ["### Colour scale", "",
                 f"{examples[0]} up to 5% · {examples[1]} 15% · {examples[2]} from 30% · {examples[3]} neutral.", "",
                 "Colour shows lag from the best value in a comparable group: green → yellow → red. "
                 "Groups entirely within 5% are grey. Colour describes effect size, not statistical significance."]
        if details_link:
            lines[-1] += f" [Comparison rules]({details_link})."
    return lines + [""]


def legend(policy=None):
    scale = " ".join(marker(name, LABELS[name if name in ("best", "worst") else "relative"])
                     for name, _ in PALETTE)
    return [
        "### Цветовая шкала", "",
        f"До 5% {scale} От 30%; " + marker("equal", LABELS["equal"]) + " нейтральная группа или неразличимое отставание.", "",
        "Шкала общая для всех таблиц, без растяжения по min-max: до 5% - зелёный; 5-15% - "
        "переход к жёлтому; 15-30% - переход к красному; от 30% - красный. Линейный переход "
        "показан девятью фиксированными SVG-ступенями. 5/15/30% - договорённые пороги величины "
        "эффекта, не статистической значимости. Худшее значение не обязано быть красным; "
        "числовые минимумы и максимумы не выделяются жирным.", "",
        "Для метрик «меньше лучше»: отставание = (значение / лучшее - 1) * 100%; "
        "для «больше лучше»: (1 - значение / лучшее) * 100%. Сравнение использует исходную "
        "неокруглённую метрику; показанные числа не меняются. При нулевой базе для «меньше лучше» "
        "ноль равен базе, положительное значение имеет неопределённое относительное отставание "
        "и красный маркер только после абсолютного порога. При «больше лучше» нулевая база "
        "означает группу нулей. Деления на ноль и вымышленного epsilon нет.", "",
        "Если вся группа укладывается в 5% от лучшего, она серая без победителя. Абсолютный "
        "порог также может сделать группу или отдельное отставание нейтральным. Серый не "
        "доказывает статистического равенства. Неизвестные значения и группы с единственным "
        "известным значением не ранжируются.", "",
        "В текущем наборе один независимый прогон на профиль/фреймворк: цвет показывает "
        "наблюдаемую величину отставания, а не статистическую значимость или устойчивый рейтинг. "
        "Последовательные тики, минутные окна и StddevMs одного прогона не являются независимыми повторами.", "",
        "При явно переданных независимых повторах база - лучшая медиана метрики отдельных "
        "прогонов, не самый удачный одиночный замер. Интервалы min-max повторов - описательный "
        "разброс, не доверительные интервалы. Отставание свыше 5% окрашивается только если "
        "консервативное разделение интервалов превышает и 5%, и выбранный абсолютный порог; "
        "иначе оно серое. Неполные повторные "
        "данные делают всю группу серой. Повторы должны иметь уникальные run_id и совпадающие "
        "условия; текущие файлы не содержат таких повторов.", "",
        "Фреймворки сравниваются только внутри одного профиля и одной метрики; для стадий - "
        "в одной стадии и профиле. Память дополнительно разделена по типу метрики, фазы - по "
        "одинаковому интервалу симуляции. Широкие аллокации включают обвязку и не означают аллокации только Step.", "",
        "Абсолютные временные пороги задаются отдельно для точной пары profile/metric в "
        "tools/space-battle/heatmap_policy.json, в ms с обоснованием EN/RU; wall_seconds "
        "переводится из ms в секунды. Общего порога для всех стадий нет. По умолчанию все "
        "пороги отключены (null): нет независимых повторов, калибровки или заданной цены "
        "улучшения для выбора обоснованных ms. Точность печати и бюджет тика не являются "
        "основанием практически существенного порога. Это описательная шкала величины эффекта.", "",
        "Численности, состав, события боя, GC-счётчики, доли стадий, тренды памяти и отношение "
        "поздней фазы к ранней остаются нейтральными. Единственный frontend replay также "
        "не ранжируется: разные операции и квантили не являются конкурирующими фреймворками.", "",
        "Маркеры - локальные SVG-изображения для GitHub Markdown без CSS и внешних сервисов; "
        "сами числа остаются текстом.", "",
    ] + (["Выбранные абсолютные пороги:"] + [
        f"- `{entry['profile']}/{entry['metric']}`: {entry['threshold_ms']} ms; " + entry["reason_ru"]
        for entry in policy.entries
    ] + [""] if policy and policy.entries else ["Выбранные абсолютные пороги: нет (все null).", ""])


def write_assets(docs):
    output = docs / "heatmap"
    output.mkdir(parents=True, exist_ok=True)
    for name, color in (*PALETTE, ("equal", EQUAL_COLOR)):
        root = ET.Element("svg", xmlns="http://www.w3.org/2000/svg",
                          width="12", height="12", viewBox="0 0 12 12")
        ET.SubElement(root, "title").text = name
        ET.SubElement(root, "rect", x="1", y="1", width="10", height="10", rx="2",
                      fill=color, stroke="#59636e", **{"stroke-width": "0.6"})
        (output / (name + ".svg")).write_text(ET.tostring(root, encoding="unicode") + "\n", encoding="utf-8")
