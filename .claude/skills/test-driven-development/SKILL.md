---
name: Test-Driven Development (TDD)
description: Use when implementing any feature or bugfix, before writing implementation code - write the test first, watch it fail, write minimal code to pass; ensures tests actually verify behavior by requiring failure first
---

<required>
*CRITICAL* Add the following steps to your Todo list using TodoWrite:

1. Write failing tests (RED phase)

2. Verify the test fails due to the behavior of the application, and NOT due to the test.
<system-reminder>If you have more than one test that you need to write, you should write all of them before moving to the GREEN phase.</system-reminder>
3. Write the minimal amount of code necessary to make the test pass (GREEN phase)
4. Verify the test now passes due to the behavior of the application.
    - If you go through three loops without making progress, switch to running `/home/dom/Downloads/repos/zipper/.claude/skills/creating-debug-tests-and-iterating`
5. Refactor the code to clean it up.
6. Verify tests still pass.
</required>

## RED - Write Failing Test

Write one minimal test showing what should happen.

<good-example>

```typescript
test('retries failed operations 3 times', async () => {
  let attempts = 0;
  const operation = () => {
    attempts++;
    if (attempts < 3) throw new Error('fail');
    return 'success';
  };

  const result = await foobar.retryOperation(operation);

  expect(result).toBe('success');
  expect(attempts).toBe(3);
});
```

Clear name, tests real behavior, one thing. Note that the tested operation is
imported -- this is a STRONG sign that this is testing something real.

</good-example>

<bad-example>

```typescript
test('retry works', async () => {
  const mock = jest
    .fn()
    .mockRejectedValueOnce(new Error())
    .mockRejectedValueOnce(new Error())
    .mockResolvedValueOnce('success');
  await retryOperation(mock);
  expect(mock).toHaveBeenCalledTimes(3);
});
```

Vague name, tests mock not code
</bad-example>

## Verify RED - Watch It Fail

```bash
npm test path/to/test.test.ts
```

Confirm:

- Test fails (not errors)
- Failure message is expected
- Fails because feature missing (not typos)

## GREEN - Minimal Code

Write simplest code to pass the test.

<good-example>
```typescript
async function retryOperation<T>(fn: () => Promise<T>): Promise<T> {
  for (let i = 0; i < 3; i++) {
    try {
      return await fn();
    } catch (e) {
      if (i === 2) throw e;
    }
  }
  throw new Error('unreachable');
}
```
Just enough to pass
</good-example>

<bad-example>
```typescript
async function retryOperation<T>(
  fn: () => Promise<T>,
  options?: {
    maxRetries?: number;
    backoff?: 'linear' | 'exponential';
    onRetry?: (attempt: number) => void;
  }
): Promise<T> {
  // YAGNI
}
```
Over-engineered
</bad-example>

Don't add features, refactor other code, or "improve" beyond the test.

## Verify GREEN - Watch It Pass

```bash
npm test path/to/test.test.ts
```

Confirm:

- Test passes
- Other tests still pass
- Output pristine (no errors, warnings)

## REFACTOR - Clean Up

After green only:

- Remove duplication
- Improve names
- Extract helpers

Keep tests green. Do not add behavior.
