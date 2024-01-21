Celbridge is a visual coding tool based on C# & .NET, with an emphasis on:

- Reducing the cognitive load of coding
- Compatibility between programming languages, libraries and runtime environments
- Extendable functionality via a plugin architecture

The current version supports text-based scripting only. The goal is to add a visual scripting
layer on top of this foundation later in development.

# Project Status

Celbridge is in early development so there will be many breaking changes with every release. 
I wouldn't recommend using it in a production environment until we reach v1.0. 

In the meantime, please do try it out and all feedback is very welcome!

When the project reaches v1.0 there will be a much stronger emphasis on maintaining backwards compatibility 
with each release.

## Who am I

I'm Chief Architect at Romero Games, a games studio based in Galway, Ireland. 

I've worked in games development for 20+ years and have a lot experience with many programming languages
and technologies, particularly C# and .NET. I created the Fungus visual scripting tool for the Unity game engine.

## Research Project

I am developing this tool as part of my Research Masters with Technical University Dublin. 

My goal is to develop Celbridge from a research project into a robust tool suitable for use 
in production environments. As my own background is in game development, I'm focussing on typical game
development use cases initially.

This research was made possible because of the sabbatical program at Romero Games, so many thanks Brenda and John Romero 
for their support.

## Roadmap

The tool will initially support scripting workflows via C#, Python, Markdown, etc. Once these workflows are
robust, I will add support for Cel Script, a novel visual scripting language.

- [x] Proof-of-concept prototype 
- [ ] Application framework (GUI framework, plugin architecture, public API)
- [ ] Basic IDE workflows (project & file management, text document editing)
- [ ] Advanced document editing (syntax highlighting, spelling correction, web views)
- [ ] Scripting support via C# Interactive
- [ ] Interactive console window
- [ ] Cel Script programming language (using Json text files transpiled to C#)
- [ ] Cel Script runtime environment
- [ ] Cel Script visual programming language (built on top of the Json implementation)
- [ ] Integrated debugger (via C# / .NET debugger)

## Visual Scripting Prototype

During 2023 I developed a proof of concept prototype of Celbridge. This helped me validate the concept and to figure out the requirements 
for the full implementation.

This video covers the main features of the prototype. This is a good way to learn about the planned features of Celbridge.

You can try this prototype by checking out the `demo-dec-23` tag of this project on Github. Warning: It's quite unstable and undocumented!



