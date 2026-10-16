"""Conservative lexical import matching, not runtime identity or type inference."""

import ast
from collections import defaultdict


DEFINITIONS = (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda, ast.ClassDef)
COMPREHENSIONS = (ast.ListComp, ast.SetComp, ast.DictComp, ast.GeneratorExp)


def dotted(node):
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        prefix = dotted(node.value)
        return f"{prefix}.{node.attr}" if prefix else None
    return None


class Scope(ast.NodeVisitor):
    def __init__(self, node, parent):
        self.node, self.parent = node, parent
        self.bindings = defaultdict(list)
        self.mutations = []
        self.redirected = set()
        self.dynamic = False
        body = node.body if not isinstance(node, ast.Lambda) else [node.body]
        self.direct = set(body)
        if hasattr(node, "args"):
            args = node.args
            for child in [*args.posonlyargs, *args.args, *args.kwonlyargs, args.vararg, args.kwarg]:
                if child is not None:
                    self.bindings[child.arg].append(None)
        for child in body:
            self.visit(child)
        for parameter in getattr(node, "type_params", []):
            self.bindings[parameter.name].append(None)

    def visit_Name(self, node):
        if isinstance(node.ctx, (ast.Store, ast.Del)):
            self.bindings[node.id].append(None)

    def visit_Attribute(self, node):
        if isinstance(node.ctx, (ast.Store, ast.Del)):
            self.mutations.append(node)
        self.generic_visit(node)

    def visit_Import(self, node):
        for alias in node.names:
            name = alias.asname or alias.name.split(".")[0]
            target = alias.name if alias.asname else name
            self.bindings[name].append((target, node.lineno) if node in self.direct else None)

    def visit_ImportFrom(self, node):
        for alias in node.names:
            if alias.name == "*":
                self.dynamic = True
                continue
            target = f"{node.module}.{alias.name}"
            self.bindings[alias.asname or alias.name].append(
                (target, node.lineno) if node in self.direct and not node.level else None)

    def visit_FunctionDef(self, node):
        self.bindings[node.name].append(None)
        # Headers execute in this scope, but are not owned-body call evidence.
        for child in [*node.decorator_list, *node.args.defaults, *node.args.kw_defaults]:
            if child is not None:
                self.visit(child)

    visit_AsyncFunctionDef = visit_FunctionDef

    def visit_ClassDef(self, node):
        self.bindings[node.name].append(None)
        for child in [*node.decorator_list, *node.bases, *node.keywords]:
            self.visit(child)

    def visit_Lambda(self, node):
        for child in [*node.args.defaults, *node.args.kw_defaults]:
            if child is not None:
                self.visit(child)

    def visit_ExceptHandler(self, node):
        if node.name:
            self.bindings[node.name].append(None)
        self.generic_visit(node)

    def visit_MatchAs(self, node):
        if node.name:
            self.bindings[node.name].append(None)
        self.generic_visit(node)

    visit_MatchStar = visit_MatchAs

    def visit_MatchMapping(self, node):
        if node.rest:
            self.bindings[node.rest].append(None)
        self.generic_visit(node)

    def visit_Global(self, node):
        self.redirected.update(node.names)

    visit_Nonlocal = visit_Global

    def visit_Call(self, node):
        if isinstance(node.func, ast.Name) and node.func.id in {"exec", "eval", "globals", "locals", "setattr", "delattr"}:
            self.dynamic = True
        self.generic_visit(node)

    def visit_ListComp(self, node):
        # Iteration variables are comprehension-local; walrus bindings are not.
        for generator in node.generators:
            self.visit(generator.iter)
            for condition in generator.ifs:
                self.visit(condition)
        for field in ("elt", "key", "value"):
            if hasattr(node, field):
                self.visit(getattr(node, field))

    visit_SetComp = visit_ListComp
    visit_DictComp = visit_ListComp
    visit_GeneratorExp = visit_ListComp


class Bindings(ast.NodeVisitor):
    def __init__(self, tree):
        self.scopes = {}
        self.current = None
        self.visit(tree)
        # Cross-scope writes and reflective syntax veto matching conservatively.
        self.redirected = set().union(*(scope.redirected for scope in self.scopes.values()))
        self.dynamic = any(scope.dynamic for scope in self.scopes.values())
        self.mutated_modules = set()
        for scope in self.scopes.values():
            for node in scope.mutations:
                target, _ = self.resolve(node, scope, mutations=False)
                if target:
                    self.mutated_modules.add(target.split(".")[0])

    def visit_Module(self, node):
        previous = self.current
        parent = previous
        if not isinstance(node, ast.ClassDef):
            while parent and isinstance(parent.node, ast.ClassDef):
                parent = parent.parent
        self.current = Scope(node, parent)
        self.scopes[node] = self.current
        # Only body scopes are measured; definition headers aren't new owners.
        body = [node.body] if isinstance(node, ast.Lambda) else node.body
        for child in body:
            self.visit(child)
        self.current = previous

    visit_FunctionDef = visit_Module
    visit_AsyncFunctionDef = visit_Module
    visit_ClassDef = visit_Module
    visit_Lambda = visit_Module

    def resolve(self, node, scope, *, shadowed=frozenset(), mutations=True):
        path = dotted(node)
        if path is None:
            return None, "dynamic-callee-expression"
        root, *suffix = path.split(".")
        if root in shadowed:
            return None, "comprehension-local-binding"
        if self.dynamic or root in self.redirected:
            return None, "reflective-or-cross-scope-binding"
        while scope:
            if root in scope.bindings:
                entries = scope.bindings[root]
                if len(entries) != 1 or entries[0] is None:
                    return None, "non-import-or-ambiguous-binding"
                target, line = entries[0]
                if node.lineno <= line:
                    return None, "import-not-before-use"
                target = ".".join([target, *suffix])
                if mutations and target.split(".")[0] in self.mutated_modules:
                    return None, "imported-module-attribute-write"
                return target, "import-spelling-only"
            scope = scope.parent
        return None, "no-lexical-import"
