"""Bounded source links for async calls; never follow or execute callback bodies."""

import ast

from async_bindings import dotted
import async_usage as usage


def shape(node):
    if isinstance(node, ast.Name):
        return "name"
    if isinstance(node, ast.Attribute):
        return "attribute-path" if dotted(node) else "attribute-on-expression"
    return {ast.Call: "call-result", ast.Subscript: "subscript", ast.Lambda: "lambda"}.get(type(node), "other-expression")


def unresolved(reason):
    return {"status": "unresolved", "reason": reason, "definition": None}


def before(definition, use):
    return (definition.end_lineno, definition.end_col_offset) < (use.lineno, use.col_offset)


class Provenance:
    def __init__(self, bindings, owners, file):
        self.bindings, self.owners, self.file = bindings, owners, file
        self.definitions, self.assignments = {}, {}
        for scope in bindings.scopes.values():
            self.definitions[scope] = {node.name: node for node in scope.direct
                if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))}
            self.assignments[scope] = {node.targets[0].id: node for node in scope.direct
                if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name)}

    def veto(self, name, shadowed):
        if name in shadowed:
            return "comprehension-local-binding"
        if self.bindings.dynamic or name in self.bindings.redirected:
            return "reflective-or-cross-scope-binding"
        return None

    def reference(self, node, scope, shadowed):
        # An import match is still only spelling; a local link is only a declaration.
        target, reason = self.bindings.resolve(node, scope, shadowed=shadowed)
        if target:
            return {"status": "import-spelling", "reason": reason, "target": target, "definition": None}
        if not isinstance(node, ast.Name):
            return unresolved("non-name-reference")
        veto = self.veto(node.id, shadowed)
        if veto:
            return unresolved(veto)
        while scope:
            if node.id in scope.bindings:
                if len(scope.bindings[node.id]) != 1:
                    return unresolved("rebound-or-ambiguous-name")
                definition = self.definitions[scope].get(node.id)
                if definition is None:
                    return unresolved("not-a-direct-function-definition")
                if definition.decorator_list:
                    return unresolved("decorated-definition")
                if not before(definition, node):
                    return unresolved("definition-not-before-reference")
                key = usage.digest(f"{self.file}|{definition.lineno}|{definition.col_offset}|{type(definition).__name__}")
                owner = self.owners.get(key)
                if owner is None:
                    return unresolved("definition-outside-selected-owners")
                return {"status": "local-definition", "reason": "unique-direct-lexical-definition",
                        "definition": {"ownerId": key, "file": self.file, "member": owner["member"],
                                       "functionKind": type(definition).__name__, **usage.extent(definition)}}
            scope = scope.parent
        return unresolved("no-local-definition")

    def wrapper(self, node, scope, shadowed, context, owner_id):
        veto = self.veto(node.id, shadowed)
        if veto:
            return unresolved(veto), None
        if context != "async-body":
            return unresolved("deferred-execution-context"), None
        if len(scope.bindings.get(node.id, [])) != 1:
            return unresolved("not-a-unique-same-owner-binding"), None
        assignment = self.assignments[scope].get(node.id)
        if assignment is None or not isinstance(assignment.value, ast.Call):
            return unresolved("not-a-direct-wrapper-assignment"), None
        if not before(assignment, node):
            return unresolved("assignment-not-before-use"), None
        construction = assignment.value
        target, _ = self.bindings.resolve(construction.func, scope)
        if target != usage.WRAPPER:
            return unresolved("unmatched-wrapper-constructor"), None
        if not construction.args or isinstance(construction.args[0], ast.Starred):
            return unresolved("unresolved-argument-layout"), None
        location = usage.extent(construction)
        return {"status": "stored-wrapper-syntax", "reason": "unique-direct-same-owner-assignment",
                "definition": {"name": node.id, "assignment": usage.extent(assignment),
                               "constructionCallId": usage.fingerprint([owner_id, location]),
                               "construction": location, "importTarget": target}}, construction.args[0]


class ContextCalls(usage.Calls):
    def __init__(self, bindings, node, owner, provenance):
        super().__init__(bindings, node, owner)
        self.provenance, self.contexts = provenance, []

    def record(self, node, context, shadowed, parent):
        super().record(node, context, shadowed, parent)
        call = self.rows[-1]
        local, wrapper, callback = None, None, None
        callback_node = None
        if call["category"] in {"offload-call", "offload-wrapper-construction", "offload-wrapped-invocation"}:
            helper = node.func if call["category"] == "offload-wrapped-invocation" else node
            if helper.args and not isinstance(helper.args[0], ast.Starred):
                callback_node = helper.args[0]
        if call["binding"]["status"] == "unresolved" and isinstance(node.func, ast.Name):
            local = self.provenance.reference(node.func, self.scope, shadowed)
            wrapper, callback_node = self.provenance.wrapper(node.func, self.scope, shadowed, context, self.owner["id"])
        if callback_node is not None:
            # A stored wrapper's callback was evaluated at construction, outside a use-site comprehension.
            callback_shadowed = frozenset() if wrapper and wrapper["status"] == "stored-wrapper-syntax" else shadowed
            callback = {"kind": type(callback_node).__name__,
                        "evaluation": "eager-call-expression" if isinstance(callback_node, ast.Call) else "callback-expression-only",
                        "reference": self.provenance.reference(callback_node, self.scope, callback_shadowed)}
        self.contexts.append({"callId": call["id"], "executionContext": context, "shape": shape(node.func),
                              "unresolvedReason": call["binding"]["reason"] if call["binding"]["status"] == "unresolved" else None,
                              "localCallee": local, "storedWrapper": wrapper, "callback": callback})

    def measure(self, node):
        measurement = super().measure(node)
        by_id = {row["callId"]: row for row in self.contexts}
        return measurement, [by_id[call["id"]] for call in measurement["calls"]]
