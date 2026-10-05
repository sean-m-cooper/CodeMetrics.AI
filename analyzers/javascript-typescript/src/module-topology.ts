// Iterative SCC traversal avoids stack overflow on long import chains and emits
// one cyclic component, not an exponential list of individual cycle paths.
export function cyclicComponents(graph: Map<string, Set<string>>): string[][] {
  const visited = new Set<string>(), order: string[] = [];
  for (const root of graph.keys()) {
    const stack: [string, boolean][] = [[root, false]];
    while (stack.length) {
      const [node, finishing] = stack.pop()!;
      if (finishing) { order.push(node); continue; }
      if (visited.has(node)) continue;
      visited.add(node); stack.push([node, true]);
      for (const target of graph.get(node) ?? []) if (!visited.has(target)) stack.push([target, false]);
    }
  }
  const reverse = new Map([...graph.keys()].map(node => [node, new Set<string>()]));
  for (const [node, targets] of graph) for (const target of targets) reverse.get(target)!.add(node);
  visited.clear();
  const components: string[][] = [];
  for (const root of order.reverse()) {
    if (visited.has(root)) continue;
    const component: string[] = [], stack = [root];
    visited.add(root);
    while (stack.length) {
      const node = stack.pop()!; component.push(node);
      for (const target of reverse.get(node) ?? []) if (!visited.has(target)) { visited.add(target); stack.push(target); }
    }
    if (component.length > 1 || graph.get(root)?.has(root)) components.push(component.sort());
  }
  return components.sort((a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);
}

interface DependencyEdge { from: string; target?: string; usage: string; resolution: string; form: string; roles: string[]; }

function referenceView(ids: string[], edges: DependencyEdge[]) {
  const graph = new Map(ids.map(id => [id, new Set<string>()]));
  const incoming = new Map(ids.map(id => [id, new Set<string>()]));
  for (const edge of edges) if (edge.resolution === "internal" && edge.usage !== "typeOnly" && edge.target) {
    graph.get(edge.from)!.add(edge.target); incoming.get(edge.target)!.add(edge.from);
  }
  const modules = ids.map(id => ({ id, internalValueFanOut: graph.get(id)!.size, internalValueFanIn: incoming.get(id)!.size }));
  const rank = (key: "internalValueFanOut" | "internalValueFanIn") => modules.filter(item => item[key] > 0)
    .sort((a, b) => b[key] - a[key] || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)).slice(0, 10);
  return { graph, modules, internalValueEdgeCount: modules.reduce((sum, node) => sum + node.internalValueFanOut, 0),
    highestFanOut: rank("internalValueFanOut"), highestFanIn: rank("internalValueFanIn") };
}

export function moduleTopology(ids: string[], edges: DependencyEdge[]) {
  const { graph, internalValueEdgeCount, ...overall } = referenceView(ids, edges);
  const { graph: _imports, ...implementation } = referenceView(ids, edges.filter(edge => edge.roles.includes("implementation")));
  const { graph: _exports, ...reExports } = referenceView(ids, edges.filter(edge => edge.roles.includes("reExport")));
  return { ...overall, cycles: cyclicComponents(graph), dependencyViews: {
    version: 2, classification: "explicit-forwarding-v2",
    implementation: { basis: "Imports/loads with local uses or no recognized forwarding; unsupported alias flow remains here.", ...implementation },
    reExports: { basis: "Direct re-exports and explicit forwarding through an unmodified local import/require binding or direct require call.", ...reExports },
    totalInternalValueEdgeCount: internalValueEdgeCount,
    sharedInternalValueEdgeCount: implementation.internalValueEdgeCount + reExports.internalValueEdgeCount - internalValueEdgeCount
  } };
}
