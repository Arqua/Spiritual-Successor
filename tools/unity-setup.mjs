#!/usr/bin/env node
/**
 * Prepare a freshly created Unity project to consume this repository.
 *
 * Unity Hub has to create the project itself - it writes ProjectSettings that
 * are Editor-version-specific - so this script picks up from there and does the
 * fiddly parts:
 *
 *   1. adds both packages to Packages/manifest.json
 *   2. copies the canonical content pack into Assets/Resources
 *   3. drops a smoke-test script into Assets
 *
 * Editing manifest.json by hand is the step most likely to go wrong: a missing
 * or extra comma leaves a file Unity will not open at all, with an error that
 * does not obviously point at the comma. Parsing and re-serializing the JSON
 * removes that whole class of mistake.
 *
 * Zero dependencies, so it runs on a fresh clone without `npm install`:
 *
 *   node tools/unity-setup.mjs
 */

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const projectDir = join(repoRoot, 'unity', 'UnityProject');
const manifestPath = join(projectDir, 'Packages', 'manifest.json');
const contentSource = join(repoRoot, 'content', 'starter.json');

const PACKAGES = {
  'com.aetherlight.core': 'file:../../Aetherlight.Core',
  'com.aetherlight.unity': 'file:../../Aetherlight.Unity',
};

const SMOKE_TEST = `using UnityEngine;
using Aetherlight.Core;
using Aetherlight.Domain;

/// <summary>
/// Proves the rules core works inside Unity: package resolution, JSON parsing,
/// content validation, and class derivation from bound motes.
///
/// Put this on any GameObject and press Play. Written by tools/unity-setup.mjs;
/// delete it once the project is real.
/// </summary>
public class CoreSmokeTest : MonoBehaviour
{
    void Start()
    {
        var asset = Resources.Load<TextAsset>("starter");
        if (asset == null)
        {
            Debug.LogError("starter.json not found in Assets/Resources. Re-run tools/unity-setup.mjs.");
            return;
        }

        var registry = ContentRegistry.From(ContentLoader.LoadPack(asset.text));
        registry.AssertValid();

        var def = registry.ActorDef("rell");
        if (def == null)
        {
            Debug.LogError("Actor 'rell' is missing from the content pack.");
            return;
        }

        var rell = Actors.Create(def, 12, registry);
        rell.Motes.Add(new MoteInstance("boulder"));
        rell.Motes.Add(new MoteInstance("cinder"));

        // Terra plus pyre resolves to the dual class. If this prints
        // "ashwarden", the whole chain underneath it works.
        Debug.Log($"class: {Actors.DeriveClass(rell, registry)?.Id}");
        Debug.Log($"maxHp: {Actors.DeriveStats(rell, registry).MaxHp}");
        Debug.Log($"items in pack: {registry.Items.Count}");
    }
}
`;

function fail(message, hint) {
  console.error(`\n  ✗ ${message}`);
  if (hint) console.error(`\n${hint}`);
  console.error('');
  process.exit(1);
}

function ok(message) {
  console.log(`  ✓ ${message}`);
}

// --- 0. the project must already exist -------------------------------------

if (!existsSync(projectDir)) {
  fail(
    `No Unity project at unity/UnityProject`,
    `  Unity Hub has to create it, because it writes ProjectSettings that are
  tied to your Editor version.

  In Unity Hub:
    Projects -> New project
    Editor version: 2021.3 LTS or newer
    Template:       2D
    Project name:   UnityProject
    Location:       ${join(repoRoot, 'unity')}

  Then quit the Editor and run this script again.`,
  );
}

if (!existsSync(manifestPath)) {
  fail(
    `unity/UnityProject exists but has no Packages/manifest.json`,
    `  That folder does not look like a Unity project. If Unity Hub refused to
  create one because the directory was not empty, delete unity/UnityProject
  and let Hub create it fresh.`,
  );
}

// --- 1. packages ------------------------------------------------------------

let manifest;
const manifestRaw = readFileSync(manifestPath, 'utf8');
try {
  manifest = JSON.parse(manifestRaw);
} catch (error) {
  fail(
    `Packages/manifest.json is not valid JSON: ${error.message}`,
    `  Unity will refuse to open the project until this is fixed. If you edited
  it by hand, a stray or missing comma is the usual cause.`,
  );
}

manifest.dependencies ??= {};

const added = [];
const alreadyThere = [];
for (const [name, reference] of Object.entries(PACKAGES)) {
  if (manifest.dependencies[name] === reference) alreadyThere.push(name);
  else {
    manifest.dependencies[name] = reference;
    added.push(name);
  }
}

// Sort so the file stays stable across runs rather than churning its diff.
manifest.dependencies = Object.fromEntries(
  Object.entries(manifest.dependencies).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)),
);

writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n', 'utf8');

if (added.length > 0) ok(`added to manifest.json: ${added.join(', ')}`);
if (alreadyThere.length > 0) ok(`already in manifest.json: ${alreadyThere.join(', ')}`);

// --- 2. content -------------------------------------------------------------

if (!existsSync(contentSource)) {
  fail(
    `content/starter.json is missing`,
    `  Run "npm run content:export" first, or check that the clone is complete.`,
  );
}

const resourcesDir = join(projectDir, 'Assets', 'Resources');
mkdirSync(resourcesDir, { recursive: true });
// Copied rather than referenced: Unity only reads what is under Assets.
writeFileSync(join(resourcesDir, 'starter.json'), readFileSync(contentSource, 'utf8'), 'utf8');
ok('copied content/starter.json -> Assets/Resources/starter.json');

// --- 3. smoke test ----------------------------------------------------------

const smokeTestPath = join(projectDir, 'Assets', 'CoreSmokeTest.cs');
if (existsSync(smokeTestPath)) {
  ok('Assets/CoreSmokeTest.cs already present, left alone');
} else {
  writeFileSync(smokeTestPath, SMOKE_TEST, 'utf8');
  ok('wrote Assets/CoreSmokeTest.cs');
}

// --- done -------------------------------------------------------------------

console.log(`
  Next, in Unity:

    1. Open the project from Unity Hub.
    2. Watch the Console (Window -> General -> Console).
         clean            -> both packages compiled
         "Safe Mode?"     -> a compile error; click Ignore and read the Console
         "package not found" -> the project is not at unity/UnityProject
    3. GameObject -> Create Empty, drag CoreSmokeTest.cs onto it, press Play.

  Passing looks like:

    class: ashwarden
    items in pack: 8

  If anything errors, send the full Console text.
`);
