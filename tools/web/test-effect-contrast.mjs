import test from "node:test";
import assert from "node:assert/strict";
import { measure, luminance, compareFamilies } from "./effect-contrast-metrics.mjs";
const rgba = (w, h, rgb) => Uint8Array.from(Array.from({ length: w * h }, () => [...rgb, 255]).flat());
test("dark and unchanged frames add no luminance", () => {
  for (const rgb of [[0, 0, 0], [240, 240, 240]]) {
    const data = rgba(2, 2, rgb), m = measure(data, data, 2, 2);
    assert.equal(m.meanAddedY, 0); assert.equal(m.strongVisibleFraction, 0); assert.ok(m.budgetPass);
  }
});
test("linear Rec709 weights real colored readability", () => {
  assert.ok(Math.abs(luminance(255, 0, 0) - 0.2126) < 1e-12);
  const dark = rgba(2, 2, [8, 12, 16]);
  const dim = measure(rgba(2, 2, [10, 50, 25]), dark, 2, 2);
  const bright = measure(rgba(2, 2, [30, 190, 85]), dark, 2, 2);
  assert.ok(bright.p75AddedY > dim.p75AddedY);
  assert.ok(bright.strongVisibleFraction > dim.strongVisibleFraction);
});
test("filled overexposure fails all three budgets", () => {
  const m = measure(rgba(64, 64, [255, 255, 255]), rgba(64, 64, [0, 0, 0]), 64, 64);
  assert.equal(m.meanAddedY, 1); assert.equal(m.hotFraction, 1);
  assert.equal(m.maxTileHotFraction, 1); assert.equal(m.budgetPass, false);
});
test("localized hot tile fails despite passing scene averages", () => {
  const dark = rgba(1024, 512, [0, 0, 0]), lit = dark.slice();
  for (let y = 0; y < 64; y++) for (let x = 0; x < 64; x++) lit.set([255, 255, 255, 255], (y * 1024 + x) * 4);
  const m = measure(lit, dark, 1024, 512);
  assert.ok(m.meanAddedY < 0.03 && m.hotFraction < 0.01);
  assert.equal(m.maxTileHotFraction, 1); assert.equal(m.budgetPass, false);
});
test("alpha-faded composited RGB arrays reduce measured energy", () => {
  const dark = rgba(4, 4, [10, 10, 10]);
  const full = measure(rgba(4, 4, [210, 130, 70]), dark, 4, 4);
  const faded = measure(rgba(4, 4, [60, 40, 25]), dark, 4, 4);
  assert.ok(faded.p75AddedY > 0 && faded.p75AddedY < full.p75AddedY);
});
test("family medians enforce fixed ten percent improvement", () => {
  const rows = value => ["projectile", "particle"].flatMap(family =>
    Array.from({ length: family === "projectile" ? 5 : 4 }, (_, kind) =>
      ({ family, kind, metrics: { p75AddedY: value } })));
  assert.ok(compareFamilies(rows(0.2), rows(0.23)).every(r => r.pass));
  assert.ok(compareFamilies(rows(0.2), rows(0.21)).every(r => !r.pass));
  assert.ok(compareFamilies(rows(0), rows(0)).every(r => !r.pass));
});
test("edge tiles use actual area and reject mismatched frames", () => {
  const dark = rgba(65, 64, [0, 0, 0]), lit = dark.slice();
  for (let y = 0; y < 64; y++) lit.set([255, 255, 255, 255], (y * 65 + 64) * 4);
  const m = measure(lit, dark, 65, 64);
  assert.equal(m.maxTileHotFraction, 1);
  assert.throws(() => measure(lit, dark.slice(4), 65, 64), /matched/);
});
