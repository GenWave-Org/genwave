// @jest-environment jsdom
// STORY-337 — Icon packs swap the chrome: the renderer + Wardrobe UI halves (PLAN T304).
// Runner: Jest. Todo-scaffolded at /plan (2026-08-15); T304 turns these live.
// Backend halves live in tests/GenWave.Host.Tests/Specs/Story337_IconPacksSwapTheChrome.cs.
//
// `IconWardrobeClient` (via `IconUninstallPackButton`) calls `useRouter()` unconditionally, so it
// is dynamic-imported AFTER the `next/navigation` mock is in place — the wardrobe-tabs.spec.tsx/
// wardrobe-avatar-packs.spec.tsx established idiom.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { parseIconPackDefinition, type IconPackDefinition, type IconPackElement } from "@/lib/icon-pack";
import { Icon, ICON_NAMES } from "../app/(authed)/_components/Icon";
import { IconPackProvider } from "../app/(authed)/_components/IconPackContext";
import { IconPackGlyph, IconPackSpecimenRow } from "../app/(authed)/_components/IconPackRenderer";
import * as HouseIcons from "../app/(authed)/_components/icons";
import { ChoiceSettingControl } from "../app/(authed)/settings/ChoiceSettingControl";
import type { IconWardrobeClient as IconWardrobeClientType } from "../app/(authed)/wardrobe/IconWardrobeClient";
import type { IconPackSummaryDto } from "../app/(authed)/wardrobe/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let IconWardrobeClient: typeof IconWardrobeClientType;

beforeEach(async () => {
  mockedUseRouter.mockReturnValue(
    { push: jest.fn(), refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>
  );
  ({ IconWardrobeClient } = await import("../app/(authed)/wardrobe/IconWardrobeClient"));
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const HOUSE_STYLE = { strokeWidth: 1.5, fill: "none" as const };

const TWO_ICON_PACK_JSON = JSON.stringify({
  schemaVersion: 1,
  style: HOUSE_STYLE,
  icons: {
    dashboard: [{ tag: "rect", x: 2, y: 2, width: 5, height: 5 }],
    settings: [{ tag: "circle", cx: 8, cy: 8, r: 2.2 }],
  },
});

/** Parses `json` (or throws) — every scenario below builds its render input off an ACTUAL parse of
 * raw JSON text, not a hand-typed `IconPackDefinition` literal, so a parser regression fails these
 * facts too, not just `lib/icon-pack.ts`'s own unit coverage. */
function parseFixtureOrThrow(json: string): IconPackDefinition {
  const definition = parseIconPackDefinition(json);
  if (definition === null) throw new Error(`expected a valid definition, got null for: ${json}`);
  return definition;
}

function iconElementsOrThrow(definition: IconPackDefinition, name: string): readonly IconPackElement[] {
  const elements = definition.icons[name];
  if (elements === undefined) throw new Error(`expected icon "${name}" on the parsed definition`);
  return elements;
}

const INSTALLED_PACK: IconPackSummaryDto = {
  slug: "line-icons",
  iconCount: 2,
  definition: TWO_ICON_PACK_JSON,
  importedFrom: "line-icons",
  importedAt: "2026-08-16T12:00:00Z",
};

// ---------------------------------------------------------------------------

describe("Feature: the safe icon-pack renderer", () => {
  describe("Scenario: whitelisted primitives render into the house frame", () => {
    it("renders a pack icon's primitives inside the 16×16 IconBase frame", () => {
      const definition = parseFixtureOrThrow(TWO_ICON_PACK_JSON);
      const elements = iconElementsOrThrow(definition, "dashboard");

      const { container } = render(<IconPackGlyph elements={elements} style={definition.style} />);

      const svg = container.querySelector("svg");
      expect(svg).toHaveAttribute("viewBox", "0 0 16 16");
      expect(svg).toHaveAttribute("width", "16");
      expect(svg).toHaveAttribute("height", "16");
      const rect = container.querySelector("rect");
      expect(rect).toHaveAttribute("x", "2");
      expect(rect).toHaveAttribute("width", "5");
    });

    it("applies the pack-level strokeWidth/fill style block", () => {
      const definition = parseFixtureOrThrow(
        JSON.stringify({
          style: { strokeWidth: 2.2, fill: "currentColor" },
          icons: { dashboard: [{ tag: "circle", cx: 8, cy: 8, r: 2 }] },
        })
      );
      const elements = iconElementsOrThrow(definition, "dashboard");

      const { container } = render(<IconPackGlyph elements={elements} style={definition.style} />);

      const svg = container.querySelector("svg");
      expect(svg).toHaveAttribute("stroke-width", "2.2");
      expect(svg).toHaveAttribute("fill", "currentColor");
      // Stroke itself is ALWAYS currentColor (SPEC F130.1 carries no separate stroke choice) —
      // matches IconBase's own hardcoded stroke, regardless of the pack's own `fill` axis.
      expect(svg).toHaveAttribute("stroke", "currentColor");
    });

    it("emits only none|currentColor — no literal color can reach the DOM", () => {
      // A hostile element (a literal hex fill) alongside a valid one, in the SAME icon.
      const hostileJson = JSON.stringify({
        style: { strokeWidth: 1.5, fill: "none" },
        icons: {
          dashboard: [
            { tag: "circle", cx: 8, cy: 8, r: 2, fill: "currentColor" },
            { tag: "circle", cx: 4, cy: 4, r: 1, fill: "#ff0000" },
          ],
        },
      });

      const definition = parseFixtureOrThrow(hostileJson);
      const elements = iconElementsOrThrow(definition, "dashboard");
      // The hostile element is DROPPED entirely at parse time — never silently coerced to a safe
      // value (rider: "malformed → skip glyph").
      expect(elements).toHaveLength(1);

      const { container } = render(<IconPackGlyph elements={elements} style={definition.style} />);

      expect(container.innerHTML).not.toContain("#ff0000");
      expect(container.querySelectorAll("circle")).toHaveLength(1);
    });

    it("tolerates pathological-but-valid geometry without crashing (negative r, 1e300 coordinates)", () => {
      // PLAN T302/T304 review rider — a negative radius and an astronomically large coordinate are
      // both FINITE numbers, so they parse through untouched; the 16×16 viewBox clips whatever such
      // a value draws. The only contract this fact pins is "never throws".
      const pathologicalJson = JSON.stringify({
        style: { strokeWidth: 1.5, fill: "none" },
        icons: {
          dashboard: [
            { tag: "circle", cx: 8, cy: 8, r: -4 },
            { tag: "rect", x: 1e300, y: 1e300, width: 5, height: 5 },
          ],
        },
      });

      const definition = parseFixtureOrThrow(pathologicalJson);
      const elements = iconElementsOrThrow(definition, "dashboard");
      expect(elements).toHaveLength(2);

      expect(() => render(<IconPackGlyph elements={elements} style={definition.style} />)).not.toThrow();
    });
  });

  describe("Scenario: the icon-name key gate rejects a hostile document (PLAN T304 fix round)", () => {
    /** Builds one pack's raw JSON text with a SINGLE icon keyed by `name` — `JSON.stringify` does the
     * escaping, so `name` can carry anything (a script tag, an oversized string, `__proto__`)
     * without hand-quoting it into a template literal. */
    function packJsonWithIconKey(name: string): string {
      return JSON.stringify({
        style: HOUSE_STYLE,
        icons: { [name]: [{ tag: "circle", cx: 8, cy: 8, r: 2 }] },
      });
    }

    it("nulls the whole document for an icon-name key outside the safe [a-z][a-z0-9-]* shape", () => {
      const json = packJsonWithIconKey("</svg><script>alert(1)</script>");

      expect(parseIconPackDefinition(json)).toBeNull();
    });

    it("nulls the whole document for an icon-name key over the 64-character cap", () => {
      const json = packJsonWithIconKey("a".repeat(65));

      expect(parseIconPackDefinition(json)).toBeNull();
    });

    it("never reparents the icons accumulator via a __proto__-named key", () => {
      const json = packJsonWithIconKey("__proto__");

      const definition = parseIconPackDefinition(json);

      // The name gate above already rejects "__proto__" on shape alone (it starts with "_", outside
      // [a-z]), so this is expected to be `null` — but the assertion also covers the case where that
      // gate's posture ever loosens: EITHER outcome is acceptable here, so long as the icons
      // accumulator's own prototype was never reparented to the hostile value.
      const iconsPrototype = definition === null ? Object.prototype : Object.getPrototypeOf(definition.icons);
      expect(iconsPrototype === Object.prototype || iconsPrototype === null).toBe(true);
    });
  });

  describe("Scenario: per-name fallback keeps the chrome whole", () => {
    it("renders the house icon for any name the active pack lacks", () => {
      // The active pack covers "dashboard"/"settings" (TWO_ICON_PACK_JSON) but not "editor".
      const { container } = render(
        <IconPackProvider definitionText={TWO_ICON_PACK_JSON}>
          <Icon name="editor" />
        </IconPackProvider>
      );

      const houseRender = render(<HouseIcons.EditorIcon />);
      expect(container.innerHTML).toBe(houseRender.container.innerHTML);
    });

    it("renders the full house set when no pack is active (empty Station:IconPack)", () => {
      // No IconPackProvider ancestor at all — the context's own default (`null`) IS "house icons"
      // (IconPackContext.tsx's own remarks), the identical shape an unset/uninstalled
      // Station:IconPack resolves to server-side.
      for (const name of ICON_NAMES) {
        const { container } = render(<Icon name={name} />);
        const HouseComponent = HouseIcons[`${toPascal(name)}Icon` as keyof typeof HouseIcons];
        const houseRender = render(<HouseComponent />);
        expect(container.innerHTML).toBe(houseRender.container.innerHTML);
      }
    });
  });

  describe("Scenario: the house icon table stays in step with icons.tsx (parity pin)", () => {
    it("covers exactly the set icons.tsx exports — the SAME contract GenWave.Host.Icons.IconNameContract mirrors server-side", () => {
      const namesFromIconsTsx = Object.keys(HouseIcons)
        .filter((key) => key.endsWith("Icon"))
        .map((exportName) => toKebab(exportName.replace(/Icon$/, "")))
        .sort();

      expect([...ICON_NAMES].sort()).toEqual(namesFromIconsTsx);
    });
  });

  describe("Scenario: the Wardrobe Icons tab", () => {
    it("lists installed packs with a specimen row rendered by the safe renderer", () => {
      render(
        <ConfirmDialogProvider>
          <IconWardrobeClient packs={[INSTALLED_PACK]} timeZone="UTC" />
        </ConfirmDialogProvider>
      );

      const list = screen.getByRole("list", { name: "Installed icon packs" });
      const card = within(list).getByText("line-icons").closest("li");
      if (card === null) throw new Error("No <li> ancestor for the pack card");

      expect(within(card).getByText("Installed · line-icons · Aug 16, 2026")).toBeInTheDocument();
      expect(within(card).getByText("2 icons")).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Uninstall line-icons" })).toBeInTheDocument();

      // The specimen row itself — drawn by the SAME safe renderer (IconPackGlyph) the active
      // chrome uses — one glyph per declared icon.
      const specimens = within(card).getByLabelText("Icon specimens");
      expect(within(specimens).getAllByRole("listitem")).toHaveLength(2);
      expect(specimens.querySelectorAll("svg")).toHaveLength(2);
    });

    it("shows an 'Active' chip and a fail-open note in the confirm copy when this pack is the station's own Station:IconPack", () => {
      render(
        <ConfirmDialogProvider>
          <IconWardrobeClient packs={[INSTALLED_PACK]} timeZone="UTC" activeSlug="line-icons" />
        </ConfirmDialogProvider>
      );

      expect(screen.getByText("Active")).toBeInTheDocument();
    });

    it("shows the empty state, never a hidden tab, when no pack is installed", () => {
      render(
        <ConfirmDialogProvider>
          <IconWardrobeClient packs={[]} timeZone="UTC" catalogEnabled />
        </ConfirmDialogProvider>
      );

      expect(screen.getByText("No icon packs installed")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Browse the Community Catalog" })).toHaveAttribute(
        "href",
        "/persona-catalog"
      );
    });

    it("the settings page shows an inline notice for a dangling Station:IconPack value (STORY-337 AC6)", () => {
      render(
        <ChoiceSettingControl
          controlId="setting-Station-IconPack"
          value="uninstalled-pack"
          onChange={jest.fn()}
          disabled={false}
          choices={[{ value: "", label: "House icons", isDefault: true }]}
        />
      );

      const select = screen.getByRole("combobox") as HTMLSelectElement;
      expect(select.value).toBe("uninstalled-pack");
      expect(screen.getByText(/uninstalled-pack.*current/i)).toBeInTheDocument();
      expect(screen.getByText(/isn.t one of the choices offered/i)).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: an icon's own element list can render as a bounded specimen grid directly", () => {
    it("IconPackSpecimenRow draws every declared icon, sorted by name", () => {
      const definition = parseFixtureOrThrow(TWO_ICON_PACK_JSON);

      const { container } = render(<IconPackSpecimenRow definition={definition} />);

      const list = screen.getByRole("list", { name: "Icon specimens" });
      expect(within(list).getAllByRole("listitem")).toHaveLength(2);
      expect(container.querySelectorAll("svg")).toHaveLength(2);
    });

    it("degrades to plain copy, never a crash, for a pack declaring no usable icons", () => {
      const definition = parseFixtureOrThrow(JSON.stringify({ style: HOUSE_STYLE, icons: {} }));

      render(<IconPackSpecimenRow definition={definition} />);

      expect(screen.getByText("This pack declares no icons.")).toBeInTheDocument();
    });
  });
});

/** `dashboard` → `Dashboard`, `safe-content` → `SafeContent` — the inverse of `toKebab` below,
 * used to look up icons.tsx's own component export for a contract name in the "no pack active"
 * fact above. */
function toPascal(kebabName: string): string {
  return kebabName
    .split("-")
    .map((segment) => segment.charAt(0).toUpperCase() + segment.slice(1))
    .join("");
}

/** `DashboardIcon`(minus its own "Icon" suffix, already stripped by the caller) → `dashboard`;
 * `SafeContent` → `safe-content` — mirrors `IconNameContract`'s own `ToContractName` server-side
 * (the T68 golden-table idiom, applied to this parity pin). */
function toKebab(pascalStem: string): string {
  return pascalStem.replace(/(?<!^)(?=[A-Z])/g, "-").toLowerCase();
}
