"""Check the published bridge with the official Python MCP client.

Install the mcp package in an isolated location and pass that path as argv[1].
Run while the published Prometheus Avalonia client is open.
"""

import asyncio
import json
import os
import sys


sys.path.insert(0, sys.argv[1])
sys.path.insert(0, os.path.join(sys.argv[1], "win32", "lib"))
sys.path.insert(0, os.path.join(sys.argv[1], "win32"))
os.add_dll_directory(os.path.join(sys.argv[1], "pywin32_system32"))

from mcp import Client, ClientSession, StdioServerParameters  # noqa: E402
from mcp.client.stdio import stdio_client  # noqa: E402


async def main() -> None:
    parameters = StdioServerParameters(command=sys.argv[2])
    async with stdio_client(parameters) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            initialized = await session.initialize()
            tools = await session.list_tools()
            names = {tool.name for tool in tools.tools}
            assert len(names) == 11, names
            assert {"get_state", "evaluate_changelist", "export_sequence"} <= names
            state = await session.call_tool("get_state", {"scope": "summary"})
            assert not state.is_error, state
            payload = json.loads(state.content[0].text)
            assert "revision" in payload, payload
            print(json.dumps({"protocol": initialized.protocol_version, "tools": len(names), "state": "ok"}))
    async with Client(parameters) as client:
        tools = await client.list_tools()
        assert len(tools.tools) == 11
        result = await client.call_tool("get_state", {"scope": "summary"})
        assert not result.is_error
        print(json.dumps({"auto_negotiation": "ok", "tools": len(tools.tools)}))


asyncio.run(main())
