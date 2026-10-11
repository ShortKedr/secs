// Fixed artistic regression budgets, not display-brightness or safety certification.
export const GUARDRAILS = Object.freeze({ meanAddedY: 0.03, hotFraction: 0.01,
  tileHotFraction: 0.25, tileSize: 64, hotY: 0.8, hotDeltaY: 0.05,
  strongContrast: 3, familyImprovement: 1.1 });
const linear = Array.from({ length: 256 }, (_, i) => {
  const s = i / 255;
  return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
});
export function luminance(r, g, b) {
  return 0.2126 * linear[r] + 0.7152 * linear[g] + 0.0722 * linear[b];
}
export function percentile(values, fraction) {
  if (!values.length) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  const index = (sorted.length - 1) * fraction;
  const lo = Math.floor(index), hi = Math.ceil(index);
  return sorted[lo] + (sorted[hi] - sorted[lo]) * (index - lo);
}
export function measure(lit, hidden, width, height) {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 ||
      lit.length !== width * height * 4 || hidden.length !== lit.length)
    throw new Error("Expected matched nonempty RGBA frames");
  const columns = Math.ceil(width / GUARDRAILS.tileSize);
  const tiles = Array.from({ length: columns * Math.ceil(height / GUARDRAILS.tileSize) },
    () => ({ hot: 0, pixels: 0 }));
  const added = [];
  let sum = 0, hot = 0, strong = 0, changed = 0;
  for (let p = 0; p < width * height; p++) {
    const i = p * 4;
    const y = luminance(lit[i], lit[i + 1], lit[i + 2]);
    const background = luminance(hidden[i], hidden[i + 1], hidden[i + 2]);
    const delta = Math.max(0, y - background);
    added.push(delta); sum += delta;
    if (delta > 0) changed++;
    if (delta > 0 && (y + 0.05) / (background + 0.05) >= GUARDRAILS.strongContrast) strong++;
    const tile = tiles[Math.floor(p / width / GUARDRAILS.tileSize) * columns +
      Math.floor((p % width) / GUARDRAILS.tileSize)];
    tile.pixels++;
    if (y >= GUARDRAILS.hotY && delta >= GUARDRAILS.hotDeltaY) { hot++; tile.hot++; }
  }
  const pixels = width * height;
  const result = { pixels, meanAddedY: sum / pixels, p75AddedY: percentile(added, 0.75),
    changedFraction: changed / pixels, strongVisibleFraction: strong / pixels,
    hotPixels: hot, hotFraction: hot / pixels,
    maxTileHotFraction: Math.max(...tiles.map(t => t.hot / t.pixels)) };
  result.budgetPass = result.meanAddedY <= GUARDRAILS.meanAddedY &&
    result.hotFraction <= GUARDRAILS.hotFraction && result.maxTileHotFraction <= GUARDRAILS.tileHotFraction;
  return result;
}
export function compareFamilies(before, after) {
  return ["projectile", "particle"].map(family => {
    const old = before.filter(s => s.family === family);
    const next = after.filter(s => s.family === family);
    if (old.length !== (family === "projectile" ? 5 : 4) || next.length !== old.length ||
        old.some(s => !next.some(n => n.kind === s.kind))) throw new Error("Missing paired sprite kinds");
    const baselineMedian = percentile(old.map(s => s.metrics.p75AddedY), 0.5);
    const candidateMedian = percentile(next.map(s => s.metrics.p75AddedY), 0.5);
    return { family, baselineMedianP75AddedY: baselineMedian, candidateMedianP75AddedY: candidateMedian,
      ratio: baselineMedian > 0 ? candidateMedian / baselineMedian : null,
      pass: baselineMedian > 0 && candidateMedian >= baselineMedian * GUARDRAILS.familyImprovement };
  });
}
