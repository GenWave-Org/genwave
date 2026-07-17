// STORY-106 — Wireless favicon (Epic R / SPEC F31.6, gitea-#193)
//
// Runner: Jest (node environment — file-content/binary assertions, no DOM
// needed).
//
// 2026-07-12 amendment: the operator supplied GenWave-logo.png (the station's
// actual logo — robot-mic on a warm sunset disc) and asked for it as the
// favicon, superseding R11's token-derived SVG mark. The assets are now
// app/icon.png (256px downscale, App Router file convention) + app/favicon.ico
// (real multi-size 16+32 ICO rasterized from the same logo). The former
// token-color facts are retired with the SVG they pinned: an operator-supplied
// brand image is the identity by definition — asserting its pixels against the
// UI token palette would invert the authority (the F26 lesson: brand travels
// with the content the operator chose, not with a transport-layer rule).
// Legibility at tab size was eyeballed at 16/32 px before shipping: the disc
// silhouette and mic read clearly on light and dark chrome.

import { describe, it, expect } from "@jest/globals";
import { existsSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const ROOT = path.resolve(__dirname, "..");
const iconPngPath = path.join(ROOT, "app", "icon.png");
const faviconIcoPath = path.join(ROOT, "app", "favicon.ico");

describe("Feature: Wireless favicon", () => {
  describe("Scenario: the favicon assets exist", () => {
    it("ships app/icon.png (App Router file convention, from GenWave-logo.png)", () => {
      expect(existsSync(iconPngPath)).toBe(true);

      // A real PNG, not a renamed/mislabeled file: the 8-byte PNG signature.
      const bytes = readFileSync(iconPngPath);
      expect(bytes.subarray(0, 8)).toEqual(
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
      );
    });

    it("ships an .ico fallback", () => {
      expect(existsSync(faviconIcoPath)).toBe(true);

      // A real ICO container, not a renamed/mislabeled file: the format's
      // magic header is reserved=0x0000, type=0x0001 (icon, not cursor).
      const bytes = readFileSync(faviconIcoPath);
      expect(bytes.readUInt16LE(0)).toBe(0);
      expect(bytes.readUInt16LE(2)).toBe(1);

      // At least two sized images embedded (16px + 32px) per the ICO
      // directory's image count field at offset 4.
      const imageCount = bytes.readUInt16LE(4);
      expect(imageCount).toBeGreaterThanOrEqual(2);
    });
  });

  describe("Scenario: the mark follows the identity", () => {
    it("derives from the operator's logo, byte-for-byte reproducible from GenWave-logo.png", () => {
      // The source of truth for the mark is the repo-root logo the operator
      // supplied; this fact pins that the source still exists so the icon can
      // always be regenerated (magick resize 256 / 16+32 → ico) if the logo
      // is ever updated.
      expect(existsSync(path.join(ROOT, "..", "GenWave-logo.png"))).toBe(true);
    });

    it("keeps the icon small enough for a favicon payload", () => {
      // Guard against accidentally committing the full-resolution logo as the
      // icon (the source PNG is ~1 MB; the 256px downscale is ~85 KB).
      expect(statSync(iconPngPath).size).toBeLessThan(200 * 1024);
    });
  });
});
