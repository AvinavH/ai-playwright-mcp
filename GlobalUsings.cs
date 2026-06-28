// Global usings — applied to every .cs file in the project

// ── Third-party ─────────────────────────────────────────────────────────────── 
global using Newtonsoft.Json;

// ── System (not covered by .NET 8 implicit usings for test projects) ──────────
global using System.Text;                 // Encoding, StringBuilder
global using System.Text.RegularExpressions;

// ── Microsoft.Playwright ──────────────────────────────────────────────────────
global using Microsoft.Playwright;

// ── Framework namespaces ──────────────────────────────────────────────────────
global using Playwright.AiFramework;
global using Playwright.AiFramework.AI;
global using Playwright.AiFramework.Core;
global using Playwright.AiFramework.StepDefinitions; // ← ADD: generated step classes
global using Playwright.AiFramework.Reporting;

// ── Reqnroll ──────────────────────────────────────────────────────────────────
global using Reqnroll;

