Smoke browser sign-in uses the exact email listed in the selected run's private
`artifacts/live-smoke/<run>/credentials.txt`, with the password in that same file.
The seeded Student email is `smoke-student@example.invalid`.

Open the browser URL printed for that run, followed by `/app/login`. Enter the
full Student email including the `smoke-` prefix, then select the smoke
organization after signing in. The shorter address without the prefix is not
a seeded account; the BFF correctly returns `401 invalid_credentials` for it.

Never paste the credential file, password, cookies, or tokens into diagnostics.
