# Microsoft Sign-In Setup

DanClient uses the Prismarine-style Microsoft Live device-code flow for Minecraft sign-in.

The launcher authenticates with the built-in Minecraft Nintendo Switch title ID
`00000000441cc96b`, then exchanges Xbox Live user, device, title, and XSTS
tokens for a Minecraft Java access token.

If Microsoft returns `400 Bad Request`, check the detailed launcher status text.
