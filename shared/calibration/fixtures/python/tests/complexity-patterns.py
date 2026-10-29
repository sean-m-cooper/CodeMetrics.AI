"""Authored calibration examples; parsed only, never executed by the analyzer."""


def straight(value):
    return value


def comprehension(values):
    return [transform(value) for value in values]


def expanded_loop(values):
    result = []
    for value in values:
        result.append(transform(value))
    return result


def filtered_comprehension(values):
    return [value for value in values if value > 0]


def expanded_filter(values):
    result = []
    for value in values:
        if value > 0:
            result.append(value)
    return result


def nested_comprehension(rows):
    return [value for row in rows for value in row if value > 0]


def expanded_nested_filter(rows):
    result = []
    for row in rows:
        for value in row:
            if value > 0:
                result.append(value)
    return result


def chained_range(value):
    return 0 < value < 10


def boolean_range(value):
    return 0 < value and value < 10


def grouped_handler(operation):
    try:
        return operation()
    except (ValueError, TypeError):
        return None


def separate_handlers(operation):
    try:
        return operation()
    except ValueError:
        return None
    except TypeError:
        return None


def cleanup(operation):
    try:
        return operation()
    finally:
        close_resource()


async def sequential_awaits(first, second):
    await first()
    return await second()


def match_membership(value):
    match value:
        case 1 | 2 | 3 | 4 | 5:
            return True
        case _:
            return False


def if_membership(value):
    if value in (1, 2, 3, 4, 5):
        return True
    return False


def class_membership(value):
    match value:
        case str() | int() | float():
            return value
        case _:
            raise TypeError()


def isinstance_membership(value):
    if isinstance(value, (str, int, float)):
        return value
    raise TypeError()


def guarded_membership(value, ready, valid):
    match value:
        case 1 | 2 | 3 if ready and valid:
            return True
        case _:
            return False


def expanded_guard(value, ready, valid):
    if value in (1, 2, 3) and ready and valid:
        return True
    return False


def wildcard_guard(value, ready):
    match value:
        case _ if ready:
            return True
        case _:
            return False


def wildcard_default(value):
    match value:
        case _:
            return value


def structural_alternatives(value):
    match value:
        case ("left", item) | (item, "right"):
            return item
        case _:
            return None


def nested_pattern_alternatives(value):
    match value:
        case ("item", 1 | 2):
            return True
        case _:
            return False


def as_bound_membership(value):
    match value:
        case (1 | 2) as selected:
            return selected
        case _:
            return None


def separate_cases(value):
    match value:
        case 1:
            return "one"
        case 2:
            return "two"
        case 3:
            return "three"
        case _:
            return "other"


def separate_ifs(value):
    if value == 1:
        return "one"
    elif value == 2:
        return "two"
    elif value == 3:
        return "three"
    return "other"


def guard_comprehension(value, values):
    match value:
        case 1 if any(item for item in values if item > 0):
            return True
        case _:
            return False


def outer(value):
    def inner(item):
        match item:
            case 1 | 2:
                return True
            case _:
                return False
    return inner(value)


def singleton_membership(value):
    match value:
        case None | True | False:
            return True
        case _:
            return False


def class_structural_alternatives(value):
    match value:
        case Point(x=0) | Point(y=0):
            return True
        case _:
            return False


def membership_with_body(value, ready):
    match value:
        case 1 | 2:
            if ready:
                return True
            return False
        case _:
            return False
