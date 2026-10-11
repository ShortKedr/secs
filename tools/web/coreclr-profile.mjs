export function summarizeProfile(profile, assemblies, symbols = "") {
  const modules = Object.fromEntries(Object.entries(assemblies ?? {}).map(([assembly, value]) => [value.name, { assembly, ...value }]));
  const native = Object.fromEntries(symbols.split(/\r?\n/).filter(Boolean).map((line) => {
    const colon = line.indexOf(":"); return [line.slice(0, colon), line.slice(colon + 1)];
  }));
  const hitCounts = new Map(), inclusive = new Map(), parents = new Map();
  for (const node of profile.nodes) for (const child of node.children ?? []) parents.set(child, node.id);
  for (const id of profile.samples ?? []) {
    hitCounts.set(id, (hitCounts.get(id) ?? 0) + 1);
    let ancestor = id;
    while (ancestor !== undefined) { inclusive.set(ancestor, (inclusive.get(ancestor) ?? 0) + 1); ancestor = parents.get(ancestor); }
  }
  const functions = profile.nodes.filter((node) => inclusive.has(node.id)).map((node) => {
    const frame = node.callFrame, file = frame.url.split("/").pop();
    const index = frame.functionName.match(/^wasm-function\[(\d+)\]$/)?.[1];
    const module = modules[file];
    const names = module?.functionIndices?.[index] ?? (file?.startsWith("dotnet.native") && native[index] ? [native[index]] : [frame.functionName]);
    return { ...frame, names, assembly: module?.assembly, samples: hitCounts.get(node.id) ?? 0, inclusiveSamples: inclusive.get(node.id) ?? 0 };
  }).sort((a, b) => b.samples - a.samples);
  const total = profile.samples?.length ?? 0;
  const interpreter = functions.filter((item) => item.names.some((name) => name.startsWith("InterpExecMethod"))).reduce((sum, item) => sum + item.samples, 0);
  const nativeSymbolized = symbols.includes("InterpExecMethod(");
  return { samples: total, nativeSymbolized, interpreterLeafSamples: interpreter,
    interpreterLeafPercent: nativeSymbolized ? (total ? 100 * interpreter / total : 0) : null, functions };
}

export function requireInterpreterFree(summary) {
  if (!summary.nativeSymbolized) throw new Error("Native interpreter symbols are required for the runtime gate");
  if (!summary.samples) throw new Error("Runtime gate has no CPU samples");
  if (summary.interpreterLeafSamples)
    throw new Error(`Runtime gate failed: ${summary.interpreterLeafSamples} interpreter leaf samples; this is mixed R2R, not an interpreter-free hot path`);
}
