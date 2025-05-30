# LiteDeploy

[![Part of LiteSuite](https://img.shields.io/badge/part%20of-LiteSuite-blue)](https://github.com/svtica/LiteSuite)
[![License: Unlicense](https://img.shields.io/badge/license-Unlicense-green.svg)](LICENSE)

**Lightweight network deployment tool for managing remote Windows systems with credential management and file operations.**

LiteDeploy is a Windows Forms application designed for system administrators to manage multiple remote computers, deploy files, execute commands, and perform system maintenance tasks across networked environments.

## Features

### Remote System Management
- **Multi-Computer Support**: Manage multiple remote Windows systems from a single interface
- **Credential Management**: Secure storage and management of authentication credentials
- **Connection Testing**: Verify network connectivity and authentication before operations
- **Session Management**: Maintain persistent connections to remote systems

### File Operations
- **File Deployment**: Copy files and folders to remote systems
- **Batch Operations**: Deploy to multiple targets simultaneously
- **Verification**: Checksum verification to ensure file integrity
- **Progress Monitoring**: Real-time progress tracking for file operations

### Remote Execution
- **Command Execution**: Run commands and scripts on remote systems
- **PowerShell Support**: Execute PowerShell scripts remotely
- **Batch Processing**: Execute commands across multiple systems
- **Output Capture**: Capture and display command output and errors

### System Information
- **Remote System Details**: Gather hardware and software information
- **Service Management**: View and control Windows services remotely
- **Process Monitoring**: Monitor running processes on remote systems
- **Performance Metrics**: Basic system performance information

## Installation

1. Download the latest release
2. Extract files to desired location
3. Run `LiteDeploy.exe`
4. **Note**: Administrative privileges may be required for some remote operations

## Usage

### Initial Setup
1. **Add Computers**: Configure target systems in the computer list
2. **Credentials**: Set up authentication credentials for remote access
3. **Test Connections**: Verify connectivity before performing operations

### File Deployment
1. Select source files or folders
2. Choose target computers from the list
3. Specify destination paths on remote systems
4. Monitor deployment progress and verify completion

### Remote Commands
1. Select target computers
2. Enter commands or browse for script files
3. Execute and monitor output from remote systems
4. Review results and error messages

## Configuration

### Computer Management
- Add computers by hostname or IP address
- Group computers for easier management
- Test connectivity and authentication

### Credential Storage
- Store credentials securely for different systems
- Support for domain and local accounts
- Credential validation before use

## Technology Stack

- **Platform**: .NET Framework (Windows Forms)
- **Language**: C#
- **Networking**: WMI, PowerShell Remoting, Windows APIs
- **Security**: Windows Credential Manager integration

## Development Status

This project is **Work in Progress** and **not actively developed**. The core functionality is operational but some features may be incomplete. Moderate contributions are accepted for improvements and bug fixes.

## System Requirements

- Windows 7 or later
- .NET Framework 4.5 or higher
- Network access to target systems
- Appropriate permissions on remote systems

## Security Considerations

- Ensure proper network security when using remote management features
- Use appropriate authentication methods for your environment
- Consider firewall and group policy impacts on remote operations
- Store credentials securely and follow organizational security policies

## License

This software is released under [The Unlicense](LICENSE) - public domain.

---

*LiteDeploy simplifies remote Windows system management for administrators and IT professionals.*

## 🌟 Part of LiteSuite

This tool is part of **[LiteSuite](https://github.com/svtica/LiteSuite)** - a comprehensive collection of lightweight Windows administration tools.

### Other Tools in the Suite:
- **[LiteTask](https://github.com/svtica/LiteTask)** - Advanced Task Scheduler Alternative  
- **[LitePM](https://github.com/svtica/LitePM)** - Process Manager with System Monitoring
- **[LiteDeploy](https://github.com/svtica/LiteDeploy)** - Network Deployment and Management
- **[LiteRun](https://github.com/svtica/LiteRun)** - Remote Command Execution Utility
- **[LiteSrv](https://github.com/svtica/LiteSrv)** - Windows Service Wrapper

### 📦 Download the Complete Suite
Get all tools in one package: **[LiteSuite Releases](https://github.com/svtica/LiteSuite/releases/latest)**

---

*LiteSuite - Professional Windows administration tools for modern IT environments.*
