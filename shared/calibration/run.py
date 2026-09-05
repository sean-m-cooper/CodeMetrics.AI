"""Run pinned, labeled analyzer fixtures and verify score/accuracy release baselines.

Uses only Python's standard library. Analyzers must already be built.
All fixture execution happens in an owned temporary directory.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
from pathlib import Path


def command(args, cwd):
    result = subprocess.run(args, cwd=cwd, capture_output=True, text=True, timeout=180)
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {args[0]}\n{result.stdout}\n{result.stderr}")


def percentile(values, fraction):
    values = sorted(values)
    position = (len(values) - 1) * fraction
    low = int(position)
    high = min(low + 1, len(values) - 1)
    return round(values[low] + (values[high] - values[low]) * (position - low), 3)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ecosystem', choices=['dotnet', 'javascript-typescript', 'all'], default='all')
    parser.add_argument('--configuration', default='Release', choices=['Debug', 'Release'])
    parser.add_argument('--record', action='store_true', help='Explicitly replace reviewed regression baselines')
    parser.add_argument('--output', type=Path, help='Write the measured report without changing the baseline')
    args = parser.parse_args()
    here = Path(__file__).resolve().parent
    repository = here.parent.parent
    manifest_bytes = (here / 'corpus.json').read_bytes().replace(b'\r\n', b'\n')
    manifest = json.loads(manifest_bytes)
    ecosystems = ['dotnet', 'javascript-typescript'] if args.ecosystem == 'all' else [args.ecosystem]
    reports = {}
    for ecosystem in ecosystems:
        samples, counters = [], {}
        for entry in manifest['entries']:
            if entry['ecosystem'] != ecosystem:
                continue
            source = here / entry['path']
            actual_files = {str(file.relative_to(source)).replace('\\', '/') for file in source.rglob('*') if file.is_file()}
            if actual_files != set(entry['sha256']):
                raise RuntimeError(f"Fixture inventory changed: {entry['id']}; review and re-pin the manifest")
            for relative, expected in entry['sha256'].items():
                if hashlib.sha256((source / relative).read_bytes().replace(b'\r\n', b'\n')).hexdigest() != expected:
                    raise RuntimeError(f"Fixture hash mismatch: {entry['id']}/{relative}")
            with tempfile.TemporaryDirectory(prefix='codemetrics-corpus-') as temporary:
                work = Path(temporary)
                shutil.copytree(source, work, dirs_exist_ok=True)
                if ecosystem == 'dotnet':
                    command(['dotnet', 'restore', entry['entryPoint'], '--ignore-failed-sources'], work)
                    analyzer = repository / 'analyzers/dotnet/src/CodeMetrics.AI/bin' / args.configuration / 'net10.0/CodeMetrics.AI.dll'
                    argv = ['dotnet', str(analyzer), '--solution', entry['entryPoint'], '--skip-dependency-probe', '--configuration', 'Release']
                else:
                    argv = ['node', str(repository / 'analyzers/javascript-typescript/dist/cli.js'), '--project', entry['entryPoint']]
                command(argv + ['--output', 'metrics.csv', '--scorecard-output', 'evidence.json'], work)
                evidence = json.loads((work / 'evidence.json').read_text())
                command(['node', str(repository / 'analyzers/javascript-typescript/dist/evidence-cli.js'), '--input', 'evidence.json'], work)
                categories = {}
                for dimension, result in evidence['dimensions'].items():
                    for finding in result['findings']:
                        key = dimension + '/' + finding['category']
                        categories[key] = categories.get(key, 0) + 1
                for rule, expected in entry['labels'].items():
                    actual = categories.get(rule, 0)
                    count = counters.setdefault(rule, {'truePositive': 0, 'falsePositive': 0, 'falseNegative': 0, 'negativeCases': 0})
                    count['truePositive'] += min(actual, expected)
                    count['falsePositive'] += max(0, actual - expected)
                    count['falseNegative'] += max(0, expected - actual)
                    count['negativeCases'] += int(expected == 0)
                samples.append({'id': entry['id'], 'population': evidence['population'],
                                'scores': {key: result.get('score') for key, result in evidence['dimensions'].items()},
                                'findings': dict(sorted(categories.items()))})
                print(f"{ecosystem}: {entry['id']} analyzed", flush=True)
        distributions = {}
        for dimension in samples[0]['scores']:
            values = [sample['scores'][dimension] for sample in samples if sample['scores'][dimension] is not None]
            distributions[dimension] = None if not values else {'count': len(values), 'median': percentile(values, .5), 'p25': percentile(values, .25), 'p10': percentile(values, .1)}
        report = {'corpusKind': manifest['kind'], 'corpusSha256': hashlib.sha256(manifest_bytes).hexdigest(),
                  'tool': evidence['tool'], 'ruleset': evidence['analysis']['ruleset'],
                  'scope': 'Only listed rules have accuracy labels. Fixture distributions do not establish cross-ecosystem comparability.',
                  'accuracy': counters, 'distributions': distributions, 'samples': samples}
        reports[ecosystem] = report
        baseline = here / 'baselines' / f'{ecosystem}.json'
        if args.record:
            baseline.parent.mkdir(exist_ok=True)
            baseline.write_text(json.dumps(report, indent=2) + '\n')
        elif not baseline.exists() or json.loads(baseline.read_text()) != report:
            raise RuntimeError(f"Regression baseline changed for {ecosystem}. Inspect a --record run on a branch before accepting new results.")
        if any(count['falsePositive'] or count['falseNegative'] for count in counters.values()):
            raise RuntimeError(f"Labeled accuracy regression in {ecosystem}: {counters}")
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(reports, indent=2) + '\n')
    print('Calibration regression checks passed. Cross-ecosystem scores remain uncalibrated.')


if __name__ == '__main__':
    main()
