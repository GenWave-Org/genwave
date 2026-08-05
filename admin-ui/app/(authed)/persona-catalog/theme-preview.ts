/**
 * The theme catalog's detail live-preview container class name (SPEC F103.5, STORY-274, PLAN T186)
 * — mirrors `GenWave.Host.Api.ThemePreviewController.ContainerSelector` verbatim (the same
 * one-literal-in-two-files idiom `admin-ui/lib/theme.ts` already uses for
 * `ThemeCatalog.CookieName`). The api's constant is a CSS selector (`.theme-live-preview`); this is
 * the bare class name a DOM element's `className` attaches — `ThemeCssComposer.ComposeScoped`'s
 * response is only ever honest CSS for an element carrying this exact class.
 */
export const THEME_PREVIEW_CONTAINER_CLASS = "theme-live-preview";
