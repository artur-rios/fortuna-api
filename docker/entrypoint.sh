#!/bin/sh
set -e

# The public API listens on 8080. The metrics listener follows FORTUNA_METRICS_PORT (default 9464;
# 0 disables the exporter, so only the API port is opened). An explicit ASPNETCORE_HTTP_PORTS wins.
API_PORT=8080
if [ -z "${ASPNETCORE_HTTP_PORTS:-}" ]; then
    metrics_port="${FORTUNA_METRICS_PORT:-9464}"
    case "$metrics_port" in
        '' | *[!0-9]*)
            echo "entrypoint: FORTUNA_METRICS_PORT must be a TCP port number." >&2
            exit 1
            ;;
    esac

    if [ "$metrics_port" -eq 0 ]; then
        ASPNETCORE_HTTP_PORTS="$API_PORT"
    elif [ "$metrics_port" -eq "$API_PORT" ]; then
        echo "entrypoint: FORTUNA_METRICS_PORT must differ from the API port $API_PORT." >&2
        exit 1
    else
        ASPNETCORE_HTTP_PORTS="$API_PORT;$metrics_port"
    fi

    export ASPNETCORE_HTTP_PORTS
fi

if [ "${FORTUNA_RUN_MIGRATIONS:-true}" = "true" ]; then
    if [ -z "$FORTUNA_DATA_CONNECTIONSTRING" ]; then
        echo "entrypoint: FORTUNA_DATA_CONNECTIONSTRING is unset; cannot apply migrations." >&2
        exit 1
    fi

    # The bundle reads FORTUNA_DATA_CONNECTIONSTRING from the environment through the design-time
    # DbContext factory, so the connection string (and its password) never appears in argv.
    echo "entrypoint: applying EF Core migrations..."
    /app/fortuna-migrate
    echo "entrypoint: migrations up to date."
fi

exec "$@"
