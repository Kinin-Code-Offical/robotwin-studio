"""asyncua as an independent OPC UA client against RobotWin's server: Basic256Sha256, a named user, read and write."""
import asyncio
import sys

from asyncua import Client, ua
from asyncua.crypto.security_policies import SecurityPolicyBasic256Sha256


async def main():
    url, cert, key, server_cert, mode = sys.argv[1:6]
    client = Client(url, timeout=10)
    client.application_uri = "urn:asyncua:interop"
    m = ua.MessageSecurityMode.SignAndEncrypt if mode == "enc" else ua.MessageSecurityMode.Sign
    await client.set_security(SecurityPolicyBasic256Sha256, cert, key, server_certificate=server_cert, mode=m)
    client.set_user("operator")
    client.set_password("op-pass")
    async with client:
        node = client.get_node("ns=1;s=PLC1.SPEED")
        print("read", await node.read_value(), flush=True)
        await node.write_value(ua.DataValue(ua.Variant(3.5, ua.VariantType.Double)))
        print("read2", await node.read_value(), flush=True)
        endpoints = await client.get_endpoints()
        print("endpoints", ",".join(f"{e.SecurityPolicyUri.split('#')[-1]}:{e.SecurityMode.name}" for e in endpoints), flush=True)
    print("OK", flush=True)


asyncio.run(main())
