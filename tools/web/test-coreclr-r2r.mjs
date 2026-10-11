import assert from "node:assert/strict";
import { test } from "node:test";
import { readBoot, functionExports, checkHotExports, checkStaticRoots } from "./audit-coreclr-r2r.mjs";
import { summarizeProfile, requireInterpreterFree } from "./coreclr-profile.mjs";

test("embedded RC1 boot is parsed as JSON, not executed", () => {
  const { config } = readBoot('unrelated();/*json-start*/{"resources":{"assembly":[]}}/*json-end*/');
  assert.deepEqual(config.resources.assembly, []);
  assert.throws(() => readBoot("no manifest"));
  assert.throws(() => readBoot("/*json-start*/{}/*json-end*//*json-start*/{}/*json-end*/"));
  assert.throws(() => readBoot("/*json-start*/process.exit()/*json-end*/"));
});

test("WASM exported function indices distinguish globals and aliases", () => {
  const bytes = Buffer.from([0,97,115,109,1,0,0,0,7,19,3,3,102,111,111,0,5,3,98,97,114,0,5,3,98,97,122,3,0]);
  assert.deepEqual(functionExports(bytes), { 5: ["foo", "bar"] });
  assert.throws(() => functionExports(bytes.subarray(0, bytes.length - 1)));
  assert.deepEqual(functionExports(Buffer.from([0,97,115,109,1,0,0,0])), {});
});

test("a runtime flag and compiled Battle.Step cannot pass missing query-loop coverage", () => {
  assert.throws(() => checkHotExports({}), /BattleBridge__Advance/);
  const functions = ["BattleBridge__Advance", "Battle_1<System___Canon>__Step", "PhysicsService__Step",
    "AiSystem<System___Canon>__Execute", "WeaponSystem<System___Canon>__Execute", "__UpdateTurrets", "World__Step"];
  const inventory = Object.fromEntries(["SpaceBattle.Web", "SpaceBattle.Core", "Aether.Physics2D"].map((name) => [name, { functions }]));
  assert.throws(() => checkHotExports(inventory), /SecsStore__ForEach/);
  inventory["SpaceBattle.SECS"] = { functions: ["SecsStore__ForEach<Identity>" ] };
  assert.throws(() => checkHotExports(inventory), /SECS \/ ForEach/);
  inventory.SECS = { functions: ["Query__ForEach<Processor>"] };
  assert.equal(checkHotExports(inventory).length, 9);
});

test("runtime evidence maps function indices and keeps interpreter samples separate", () => {
  const frame = (functionName, url) => ({ functionName, url });
  const profile = { nodes: [
    { id: 1, callFrame: frame("root", ""), children: [2] },
    { id: 2, callFrame: frame("wasm-function[4]", "http://local/Core.abc.wasm"), children: [3] },
    { id: 3, callFrame: frame("wasm-function[7]", "http://local/dotnet.native.abc.wasm") },
  ], samples: [3, 3, 2] };
  const result = summarizeProfile(profile, { Core: { name: "Core.abc.wasm", functionIndices: { 4: ["Battle__Step"] } } }, "7:InterpExecMethod(frame)");
  assert.equal(result.samples, 3);
  assert.equal(result.interpreterLeafSamples, 2);
  assert.equal(result.functions.find((f) => f.names.includes("Battle__Step")).inclusiveSamples, 3);
  assert.equal(result.functions.find((f) => f.names.includes("Battle__Step")).samples, 1);
  assert.equal(summarizeProfile({ nodes: [], samples: [] }, {}).interpreterLeafPercent, null);
  assert.throws(() => requireInterpreterFree(result), /mixed R2R/);
  assert.throws(() => requireInterpreterFree({ samples: 3, interpreterLeafSamples: 0 }), /symbols/);
  assert.throws(() => requireInterpreterFree({ nativeSymbolized: true, samples: 0 }), /no CPU samples/);
  assert.doesNotThrow(() => requireInterpreterFree({ nativeSymbolized: true, samples: 3, interpreterLeafSamples: 0 }));
});

test("static roots require every concrete processor and component accessor", () => {
  const roots = [
    { key: "ai", method: "Void Store::ForEach<A,B,P>(P&)", kinds: ["store-call"], genericArguments: ["[Core]SpaceBattle.Identity", "[Core]SpaceBattle.Battle`1/AiSystem<[Adapter]Store>"] },
    { key: "get", method: "T Store::Get<T>(Entity)", kinds: ["store-call"], genericArguments: ["[Core]SpaceBattle.Transform"] },
  ];
  const manifest = { provenance: "static-il-roots-not-measured-pgo", roots };
  const inventory = { "SpaceBattle.SECS": { functions: ["SecsStore__ForEach_1<A,B,Core_AiSystem<System___Canon>>", "SecsStore_Bridge_3<A,B,Core_AiSystem<System___Canon>>__Execute", "SecsStore__Get<SpaceBattle_Core_SpaceBattle_Transform>"] }, SECS: { functions: ["Query__ForEach<Bridge_Core_AiSystem<System___Canon>>"] } };
  assert.deepEqual(checkStaticRoots(inventory, manifest), ["ai", "get"]);
  inventory["SpaceBattle.SECS"].functions.pop();
  assert.throws(() => checkStaticRoots(inventory, manifest), /Get<T>/);
  inventory.SECS.functions = [];
  assert.throws(() => checkStaticRoots(inventory, manifest), /AiSystem/);
  assert.throws(() => checkStaticRoots(inventory, { roots }), /provenance/);
});
