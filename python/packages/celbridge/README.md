# Celbridge CLI

A command-line interface for managing Celbridge projects.

## Overview

The Celbridge CLI provides a suite of commands for working with Celbridge projects.

## Current Implementation

Currently implements the `version` command as a demonstration of the CLI architecture:

- Multiple output formats (`--format json|text`)
- Clean command structure
- Extensible design for adding new commands

## Usage

The Celbridge CLI is typically installed as part of the Celbridge application. You can also install it directly:

```bash
# From PyPI (when published)
pip install celbridge

# For development
pip install -e packages/celbridge
```

Basic commands:

```bash
# Display version information
celbridge version
celbridge version --format json
```
