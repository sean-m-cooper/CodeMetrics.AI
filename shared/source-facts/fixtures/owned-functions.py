def straight(value):
    return value


def branches(value):
    if value > 0:
        return value
    if value == 0:
        return 1
    if value == -1:
        return 2
    if value == -2:
        return 3
    return -value


def outer(value):
    def inner(item):
        return item if item else 0
    return inner(value)
