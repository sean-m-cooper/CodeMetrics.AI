"""Run only this authored fixture when regenerating the coverage report."""

import asyncio
import sample

assert sample.partial(1) == 1
assert sample.full(1) == 2
assert sample.ignored(True) == 1
assert sample.outer() == 1
assert sample.multi_line() == 3
assert asyncio.run(sample.asynchronous(False)) == 0
assert sample.statements_on_one_line() == 2
assert sample.with_lambda() == 2
assert sample.Worker().run(True) == 1
