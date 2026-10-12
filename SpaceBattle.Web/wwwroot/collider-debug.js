export const colliderColors = ["#77ef9c", "#ff8ee7", "#9aabb5"];
const headerValues = 4, rowValues = 9;

export function readColliderBuffer(buffer, pointer) {
  if (!Number.isInteger(pointer) || pointer < 0 || pointer % 8 || pointer + headerValues * 8 > buffer.byteLength)
    throw new Error("Invalid collider buffer pointer");
  const header = new Float64Array(buffer, pointer, headerValues), count = header[1];
  if (header[0] !== 1 || !Number.isInteger(count) || count < 0 || header[2] !== rowValues ||
      header[3] !== (headerValues + count * rowValues) * 8 || pointer + header[3] > buffer.byteLength)
    throw new Error("Invalid collider buffer header");
  const rows = new Float64Array(buffer, pointer + headerValues * 8, count * rowValues);
  for (let i = 0; i < rows.length; i += rowValues) {
    for (let j = 0; j < rowValues; j++) if (!Number.isFinite(rows[i + j])) throw new Error("Non-finite collider geometry");
    if (!Number.isInteger(rows[i]) || ![0, 1, 2].includes(rows[i + 1]) || ![0, 1].includes(rows[i + 2]) ||
        rows[i + 6] < 0 || rows[i + 7] < 0 || rows[i + 8] < 0)
      throw new Error("Invalid collider geometry");
  }
  return rows;
}

export function colliderSummary(rows) {
  const counts = { ships: 0, projectiles: 0, debris: 0, disks: 0, rectangles: 0 };
  for (let i = 0; i < (rows?.length || 0); i += rowValues) {
    counts[["ships", "projectiles", "debris"][rows[i + 1]]]++;
    counts[rows[i + 2] === 0 ? "disks" : "rectangles"]++;
  }
  return counts;
}

export function colliderDetails(rows) {
  const result = [];
  for (let i = 0; i < (rows?.length || 0); i += rowValues) result.push({
    serial: rows[i], kind: ["ship", "projectile", "debris"][rows[i + 1]],
    shape: rows[i + 2] === 0 ? "disk" : "rectangle", x: rows[i + 3], y: rows[i + 4],
    angle: rows[i + 5], radius: rows[i + 6], halfLength: rows[i + 7], halfWidth: rows[i + 8],
  });
  return result;
}

export function drawColliders(ctx, rows, camera) {
  if (!rows) return;
  const { x: cx, y: cy, scale, width, height, originY } = camera;
  ctx.save();
  ctx.globalAlpha = 0.9;
  ctx.lineWidth = 1.25;
  ctx.setLineDash([]);
  for (let i = 0; i < rows.length; i += rowValues) {
    const x = width / 2 + (rows[i + 3] - cx) * scale,
      y = originY - (rows[i + 4] - cy) * scale,
      rectangle = rows[i + 2] === 1,
      extent = (rectangle ? Math.hypot(rows[i + 7], rows[i + 8]) : rows[i + 6]) * scale;
    if (x + extent < 0 || x - extent > width || y + extent < 0 || y - extent > height) continue;
    ctx.strokeStyle = colliderColors[rows[i + 1]];
    if (rectangle) {
      ctx.save();
      ctx.translate(x, y);
      ctx.rotate(-rows[i + 5]);
      ctx.strokeRect(-rows[i + 7] * scale, -rows[i + 8] * scale,
        rows[i + 7] * 2 * scale, rows[i + 8] * 2 * scale);
      ctx.restore();
    } else {
      ctx.beginPath();
      ctx.arc(x, y, rows[i + 6] * scale, 0, Math.PI * 2);
      ctx.stroke();
    }
  }
  ctx.restore();
}
