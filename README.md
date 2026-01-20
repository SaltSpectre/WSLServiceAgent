# 🐧 WSL Service Agent 

### *WSL Service Agent: Keeping your WSL distributions alive, one sleep at a time*

## Quick Start

**TL;DR**: Keep your WSL distributions running in the background without Docker Desktop. Designed for running Linux services, Docker containers, or any background processes that need persistent WSL instances.

---

## Overview

WSL Service Agent solves a common problem: **WSL distributions terminate when no interactive terminal is active**. This service agent addresses this by:

- Creating a hidden interactive WSL terminal
- Running `sleep infinity` to maintain the distribution's active state
- Providing a clean system tray interface for management
- Operating transparently without interfering with normal WSL usage
- Restarting the instance if an *oopsie* happens

### Use Cases:
- Running Docker containers without Docker Desktop
- Hosting web services in WSL
- Background data processing tasks
- Development environments that need persistent state

### How It Works:
The agent launches a hidden WSL session that executes an infinite sleep command, effectively "tricking" WSL into thinking there's an active user session. This keeps your WSL Linux distribution running continuously while remaining completely invisible to your normal workflow.

---

## Features 

- **Persistent WSL Sessions**: Keeps distributions alive indefinitely
- **System Tray Management**: Clean, minimal, intuitive interface
- **Zero Interference**: Works alongside your normal WSL usage
- **Multiple Instance Support**: Run several distributions simultaneously. 
- **Auto-start Support**: Launch on logon

---

## Configuration

All settings are managed through the `config.json` file. 

- **EnabledDistros**: Distributions that WSL Service Agent will keep alive. Enter the distribution names as used by WSL. If no config exists, the menu will not be populated. Update the configuration and restart the app.

Sample config.json:
```json
{
  "EnabledDistros": [
    "Ubuntu",
    "Arch"
  ]
}
```

---

## License

This project is proudly open source under the MIT License
