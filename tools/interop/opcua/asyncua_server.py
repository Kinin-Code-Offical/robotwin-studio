"""asyncua as an independent OPC UA server for RobotWin's client: Basic256Sha256 SignAndEncrypt only, user names only."""
import asyncio
import sys

from asyncua import Server, ua
from asyncua.server.user_managers import UserManager
from asyncua.crypto.permission_rules import User, UserRole


class Users(UserManager):
    def get_user(self, iserver, username=None, password=None, certificate=None):
        if username == "operator" and password == "op-pass":
            return User(role=UserRole.User)
        return None


async def main():
    url, cert, key = sys.argv[1:4]
    server = Server(user_manager=Users())
    await server.init()
    server.set_endpoint(url)
    server.set_security_policy([ua.SecurityPolicyType.Basic256Sha256_SignAndEncrypt])
    server.set_security_IDs(["Username"])
    await server.load_certificate(cert)
    await server.load_private_key(key)
    idx = await server.register_namespace("urn:interop")
    plant = await server.nodes.objects.add_object(idx, "Plant")
    level = await plant.add_variable(ua.NodeId("Plant.Level", idx), "Level", 4.5)
    await level.set_writable()
    async with server:
        print("READY", idx, flush=True)
        await asyncio.get_running_loop().run_in_executor(None, sys.stdin.readline)


asyncio.run(main())
