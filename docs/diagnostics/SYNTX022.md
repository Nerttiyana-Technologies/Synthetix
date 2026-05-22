# SYNTX022: Member cannot be updated

**Severity:** Warning

An existing-instance update method cannot set a member because it is init-only or `required`. Those members can only be set while an object is being created, so on an existing instance the member is left unchanged.

## Cause

A `void Update(source, target)` method has a target member that is init-only or `required`.

## How to fix it

If the member must be set, use a normal mapping method that constructs a new target instead of an update method. If leaving it unchanged is fine, the warning can be suppressed.
