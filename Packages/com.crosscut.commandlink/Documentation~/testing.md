# Testing

This package includes lightweight smoke tests under `Tests/Runtime` and `Tests/Editor`.

## Running Package Tests In A Host Project

To expose package tests in Unity Test Runner, add the package name to the host project's
`Packages/manifest.json`:

```json
{
  "testables": [
    "com.crosscut.commandlink"
  ]
}
```

After that, open Unity Test Runner and run the runtime and editor test suites as usual.

## Current Coverage

- Runtime tests validate small pure-code contracts that should compile and behave
  consistently in any host project.
- Editor tests validate that package editor tooling opens successfully inside the editor.

These tests are intentionally small so the package keeps a stable baseline while larger
integration coverage is added later.
