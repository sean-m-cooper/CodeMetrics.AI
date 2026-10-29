"""Authored fixture for coverage.py line attribution; not a corpus project."""


def partial(value):
    """Only the positive branch is exercised."""
    if value > 0:
        return 1
    return -1


def untouched(value):
    if value:
        return 1
    return 0


def full(value):
    return value + 1


def ignored(value):
    if value:
        return 1
    return 0  # pragma: no cover


def outer():
    def inner(value):
        if value:
            return 1
        return 0
    return inner(True)


def one_line(): return 1


def multi_line():
    return (
        1 +
        2
    )


async def asynchronous(value):
    if value:
        return 1
    return 0


def statements_on_one_line():
    value = 1; value += 1; return value


def documentation_only():
    """No executable body statements."""


def with_lambda():
    operation = lambda value: value + 1
    return operation(1)


class Worker:
    def run(self, value):
        if value:
            return 1
        return 0
