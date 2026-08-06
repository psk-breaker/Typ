# TYP
A minimalist, forward-only zen writing application built to silence your inner critic and get your first draft onto the page. By removing the ability to constantly backspace, revise, and overthink, Typ forces you to maintain creative momentum.

Built with C# and WPF as a native Windows desktop application.

## 🚀 Core Features
### 1. Uninhibited, Forward-Only Writing
The first draft editor employs a strict forward-only mechanic to keep you focused on generating content rather than editing it prematurely.

As illustrated below, the system behaves as follows:
<img width="1410" height="812" alt="image" src="https://github.com/user-attachments/assets/3e7e0007-d627-4033-91c3-d609d0a4328d" />

<img width="774" height="598" alt="20260806-2032-42 0970652" src="https://github.com/user-attachments/assets/3a1852d2-1c8a-4516-a6dc-b4cc0b353cbf" />


Deleteable1 -> The current word can be modified or deleted.

NotDeleteable 1 -> Once a space is entered, the text becomes locked history.

### 2. Structured Draft Editing Pipeline
When you are ready to organize, your workspace provides a clean, column-based pipeline to manage your writing workflow hierarchically:

Novels: Manage multiple large-scale projects.

Drafts: Keep track of different iterations or brainstorming logs.

Chapters: Breakdown your work into granular chunks.

The Promote Button: Use the Promote 🚀 action to advance your writing through the structural pipeline.

### 3. Zen Focus & Session Targets
Goal Setting: Set word count targets and countdown session timers to gamify and drive your daily writing habits.

Distraction-Free UI: A beautiful, dark-themed interface designed to keep your eyes relaxed and focused solely on the words.

## 📸 Screenshots
### Workspace & Organization Pipeline
The main interface features a sleek multi-column layout separating your projects, draft stages, and live editor.

### The Forward-Only Ruleset
A breakdown of how the editor restricts backspacing to keep your momentum going forward.

## 🛠️ Tech Stack
Language: C#

Framework: WPF (.NET 8.0 / .NET 9.0)

OS Target: Windows Desktop

## 🏃‍♂️ Getting Started
Prerequisites
Windows 10 or 11

.NET Desktop Runtime

## Installation / Building from Source
Clone the repository:

``` Bash
git clone https://github.com/psk-breaker/Typ.git
```
Open the solution file (Writing_App.sln) in Visual Studio.

Build and run the project using the Release configuration, using:
```Bash
dotnet build
dotnet run
```

## 🤝 Contributing
This is an open-source project! If you want to propose new features for the draft pipeline, optimize the text-locking engine, or fix bugs, feel free to fork the repository and open a Pull Request.
