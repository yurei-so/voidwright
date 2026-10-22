#!/usr/bin/python3
"""Retain historical definition identities without allowing old GPU runners."""
import json
print(json.dumps({'status':'superseded','reason':'Prototype runner retired; use a reviewed definition with direct PID ownership. No CUDA work started.'}))
raise SystemExit(78)
