// parse.mjs — ts-morph AST walker; emits IR-shaped JSON to stdout.
// Usage: node parse.mjs <abs-path-1.ts> [<abs-path-2.ts> ...]
// Output: single process.stdout.write of JSON array of TsFile-shaped objects.

import { Project, SyntaxKind } from "ts-morph";
import { readFileSync } from "fs";

// ---------------------------------------------------------------------------
// Cookie extraction
// ---------------------------------------------------------------------------

/**
 * Scans leading comment ranges of a node for lines that start with `// @ct:`.
 * Returns the stripped annotation strings (e.g. "@ct:map=Foo").
 * @param {import("ts-morph").Node} node
 * @returns {string[]}
 */
function extractCookies(node) {
  const sourceFile = node.getSourceFile();
  const fullText = sourceFile.getFullText();
  const leadingRanges = node.getLeadingCommentRanges();
  const cookies = [];
  for (const range of leadingRanges) {
    const commentText = fullText.slice(range.getPos(), range.getEnd());
    for (const rawLine of commentText.split("\n")) {
      const line = rawLine.trim();
      if (line.startsWith("// @ct:")) {
        cookies.push(line.slice(3)); // strip "// " → "@ct:..."
      }
    }
  }
  return cookies;
}

// ---------------------------------------------------------------------------
// Type reference helpers
// ---------------------------------------------------------------------------

/**
 * Resolves a type text for a node using getType().getText() with a fallback.
 * @param {import("ts-morph").Node} node  The node whose type we want.
 * @param {import("ts-morph").Node} [ctx] Context node for getText (defaults to node).
 * @returns {string}
 */
function resolveTypeText(node, ctx) {
  try {
    return node.getType().getText(ctx ?? node);
  } catch {
    try {
      return node.getText();
    } catch {
      return "unknown";
    }
  }
}

/**
 * Builds a TsTypeRef-shaped plain object from a type text string.
 * This is a best-effort structural decomposition; full generic parsing is left to the C# layer.
 * @param {string} text
 * @returns {object}
 */
function buildTypeRef(text) {
  const cleanText = text.trim();
  const isArray = cleanText.endsWith("[]") || cleanText.startsWith("Array<");
  // Strip trailing [] or Array<...> wrapper to get base name
  let inner = cleanText;
  if (cleanText.endsWith("[]")) {
    inner = cleanText.slice(0, -2).trim();
  } else if (cleanText.startsWith("Array<") && cleanText.endsWith(">")) {
    inner = cleanText.slice(6, -1).trim();
  }
  // Extract generic base name and type args
  const genericIdx = inner.indexOf("<");
  let name = genericIdx >= 0 ? inner.slice(0, genericIdx).trim() : inner;
  let typeArgs = [];
  if (genericIdx >= 0 && inner.endsWith(">")) {
    const argsText = inner.slice(genericIdx + 1, -1);
    typeArgs = splitTopLevelComma(argsText).map(buildTypeRef);
  }
  return { text: cleanText, name, typeArgs, isArray };
}

/**
 * Splits a comma-separated type argument list respecting nested angle brackets.
 * @param {string} s
 * @returns {string[]}
 */
function splitTopLevelComma(s) {
  const parts = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < s.length; i++) {
    const ch = s[i];
    if (ch === "<") depth++;
    else if (ch === ">") depth--;
    else if (ch === "," && depth === 0) {
      parts.push(s.slice(start, i).trim());
      start = i + 1;
    }
  }
  parts.push(s.slice(start).trim());
  return parts.filter(p => p.length > 0);
}

// ---------------------------------------------------------------------------
// Node walkers
// ---------------------------------------------------------------------------

/**
 * @param {import("ts-morph").ParameterDeclaration} param
 * @returns {object}
 */
function walkParameter(param) {
  return {
    name: param.getName(),
    type: buildTypeRef(resolveTypeText(param, param)),
    isOptional: param.isOptional(),
    isRest: param.isRestParameter(),
    hasInitializer: param.hasInitializer(),
    cookies: extractCookies(param),
  };
}

/**
 * @param {import("ts-morph").PropertyDeclaration | import("ts-morph").PropertySignature} prop
 * @returns {object}
 */
function walkProperty(prop) {
  return {
    name: prop.getName(),
    type: buildTypeRef(resolveTypeText(prop, prop)),
    isOptional: prop.hasQuestionToken?.() ?? false,
    isReadonly: prop.isReadonly?.() ?? false,
    isStatic: prop.isStatic?.() ?? false,
    accessibility: prop.getScope?.() ?? "public",
    cookies: extractCookies(prop),
  };
}

/**
 * @param {import("ts-morph").MethodDeclaration | import("ts-morph").MethodSignature} method
 * @returns {object}
 */
function walkMethod(method) {
  let returnTypeText;
  try {
    returnTypeText = method.getReturnType().getText(method);
  } catch {
    returnTypeText = "void";
  }
  return {
    name: method.getName(),
    returnType: buildTypeRef(returnTypeText),
    parameters: method.getParameters().map(walkParameter),
    typeParameters: method.getTypeParameters?.().map(tp => tp.getName()) ?? [],
    isAsync: method.isAsync?.() ?? false,
    isStatic: method.isStatic?.() ?? false,
    isOptional: method.hasQuestionToken?.() ?? false,
    isAbstract: method.isAbstract?.() ?? false,
    accessibility: method.getScope?.() ?? "public",
    cookies: extractCookies(method),
  };
}

/**
 * @param {import("ts-morph").ClassDeclaration} cls
 * @returns {object}
 */
function walkClass(cls) {
  const baseExpr = cls.getBaseClass?.();
  return {
    name: cls.getName() ?? "(anonymous)",
    typeParameters: cls.getTypeParameters().map(tp => tp.getName()),
    baseClass: baseExpr?.getName() ?? null,
    implements: cls.getImplements().map(i => i.getText()),
    properties: cls.getProperties().map(walkProperty),
    methods: cls.getMethods().map(walkMethod),
    isAbstract: cls.isAbstract(),
    isExported: cls.isExported(),
    cookies: extractCookies(cls),
  };
}

/**
 * @param {import("ts-morph").InterfaceDeclaration} iface
 * @returns {object}
 */
function walkInterface(iface) {
  return {
    name: iface.getName(),
    typeParameters: iface.getTypeParameters().map(tp => tp.getName()),
    extends: iface.getExtends().map(e => e.getText()),
    properties: iface.getProperties().map(walkProperty),
    methods: iface.getMethods().map(walkMethod),
    isExported: iface.isExported(),
    cookies: extractCookies(iface),
  };
}

/**
 * @param {import("ts-morph").EnumMember} member
 * @returns {object}
 */
function walkEnumMember(member) {
  let value = null;
  try {
    const init = member.getInitializer();
    if (init) value = init.getText();
  } catch { /* ignore */ }
  return {
    name: member.getName(),
    value,
    cookies: extractCookies(member),
  };
}

/**
 * @param {import("ts-morph").EnumDeclaration} en
 * @returns {object}
 */
function walkEnum(en) {
  return {
    name: en.getName(),
    isConst: en.isConstEnum(),
    isExported: en.isExported(),
    members: en.getMembers().map(walkEnumMember),
    cookies: extractCookies(en),
  };
}

/**
 * @param {import("ts-morph").FunctionDeclaration} fn
 * @returns {object}
 */
function walkFunction(fn) {
  let returnTypeText;
  try {
    returnTypeText = fn.getReturnType().getText(fn);
  } catch {
    returnTypeText = "void";
  }
  return {
    name: fn.getName() ?? "(anonymous)",
    returnType: buildTypeRef(returnTypeText),
    parameters: fn.getParameters().map(walkParameter),
    typeParameters: fn.getTypeParameters().map(tp => tp.getName()),
    isAsync: fn.isAsync(),
    isExported: fn.isExported(),
    cookies: extractCookies(fn),
  };
}

/**
 * @param {import("ts-morph").TypeAliasDeclaration} alias
 * @returns {object}
 */
function walkTypeAlias(alias) {
  let typeText;
  try {
    const node = alias.getTypeNode();
    typeText = node ? node.getText() : alias.getType().getText(alias);
  } catch {
    typeText = "unknown";
  }
  return {
    name: alias.getName(),
    typeParameters: alias.getTypeParameters().map(tp => tp.getName()),
    typeText,
    isExported: alias.isExported(),
    cookies: extractCookies(alias),
  };
}

/**
 * @param {import("ts-morph").ImportDeclaration} imp
 * @returns {object}
 */
function walkImport(imp) {
  const namedImports = imp.getNamedImports().map(n => n.getName());
  const defaultImport = imp.getDefaultImport()?.getText() ?? null;
  const nsImport = imp.getNamespaceImport()?.getText() ?? null;
  return {
    moduleSpecifier: imp.getModuleSpecifierValue(),
    namedImports,
    defaultImport,
    namespaceImport: nsImport,
    cookies: extractCookies(imp),
  };
}

/**
 * @param {import("ts-morph").SourceFile} sourceFile
 * @returns {object}
 */
function walkSourceFile(sourceFile) {
  return {
    filePath: sourceFile.getFilePath(),
    imports: sourceFile.getImportDeclarations().map(walkImport),
    classes: sourceFile.getClasses().map(walkClass),
    interfaces: sourceFile.getInterfaces().map(walkInterface),
    enums: sourceFile.getEnums().map(walkEnum),
    functions: sourceFile.getFunctions().map(walkFunction),
    typeAliases: sourceFile.getTypeAliases().map(walkTypeAlias),
    cookies: [],
  };
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

const _args = process.argv.slice(2);
const _pathsFileArg = _args.find(a => a.startsWith("--paths-file="));
const filePaths = _pathsFileArg
  ? JSON.parse(readFileSync(_pathsFileArg.slice("--paths-file=".length), "utf8"))
  : _args;
if (filePaths.length === 0) {
  process.stderr.write("parse.mjs: no input files\n");
  process.exit(1);
}

const project = new Project({
  skipAddingFilesFromTsConfig: true,
  compilerOptions: {
    allowJs: false,
    skipLibCheck: true,
  },
});

for (const fp of filePaths) {
  project.addSourceFileAtPath(fp);
}

const result = project.getSourceFiles().map(walkSourceFile);
process.stdout.write(JSON.stringify(result));
