#!/bin/bash
# usage: logsummary.sh <sandbox AKML SQL folder>   - the log facts that are stable from run to run
d="$1/logs"
echo "engine launches: $(cat "$d"/*.log | grep -c 'Engine launched')"
echo "analysis results (findings, in order): $(cat "$d"/*.log | grep -h 'Analysis complete' | sort | sed -E 's/.*complete: ([0-9]+) findings.*/\1/' | tr '\n' ' ')"
echo "history records sent: $(cat "$d"/*.log | grep -c 'history record sent to engine')"
echo "warnings: $(cat "$d"/*.log | grep -c '\[WRN\]')  errors: $(cat "$d"/*.log | grep -c '\[ERR\]')"
