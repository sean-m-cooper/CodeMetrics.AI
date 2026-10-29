# Authored coverage.py fixture

`sample.py` and `exercise.py` are project-authored test inputs. `coverage.json` is the unedited coverage.py 7.6.1 format-3 output generated with Python 3.12 on 2026-10-12. Only this fixture was executed; no external corpus tests or project dependencies were installed.

To regenerate, use an isolated Python 3.12 environment with `coverage==7.6.1`, change into this directory, then run:

```powershell
python -m coverage run --branch --data-file=E:/temp/python-fixture.coverage exercise.py
python -m coverage json --data-file=E:/temp/python-fixture.coverage --include=sample.py -o coverage.json
```

On other systems choose a temporary data-file path appropriate to that system. Regeneration changes the report timestamp; do not regenerate during ordinary tests. The importer tests copy the source to a temporary root and read the checked-in JSON using the standard library only.

`one_line` is never called. Nevertheless, its line appears in `executed_lines` because the definition executes. That is why the importer withholds its estimate. `untouched` has three explicitly missing body lines, so its coverage is genuinely reported as zero. `partial`, `outer.inner`, `asynchronous`, and `Worker.run` each have two of three executable body lines covered, while their branch-inclusive reported percentage is 60%. The importer reports the line-only fraction 2/3. `ignored` keeps its excluded line visible without reducing its CC. `multi_line` and `statements_on_one_line` each have one reportable body line.
