#!/bin/sh
set -e

if [ "${FORTUNA_RUN_MIGRATIONS:-true}" = "true" ]; then
    if [ -z "$FORTUNA_DATA_CONNECTIONSTRING" ]; then
        echo "entrypoint: FORTUNA_DATA_CONNECTIONSTRING is unset; cannot apply migrations." >&2
        exit 1
    fi

    echo "entrypoint: applying EF Core migrations..."
    /app/fortuna-migrate --connection "$FORTUNA_DATA_CONNECTIONSTRING"
    echo "entrypoint: migrations up to date."
fi

exec "$@"
