# Renatus Front Design System

## 0. Research Log
- Embedded refs: existing MudBlazor shell plus the frontend soft-surface guidance; selected a calm editorial split for the public hero and a nested soft card for auth.
- Lazyweb: skipped by task scope; browser QA is explicitly deferred.
- Imagen drafts: skipped; configurable site imagery is supplied by the public site contract.

## 1. Atmosphere & Identity
Renatus Front is a quiet gateway into a community: evening image-led public pages, translucent blue-violet glass, and restrained ink surfaces give the shell a sense of place without competing with the site-configured content. The signature is a top-only navigation bar with no drawer affordance, plus a split auth card where the left panel is the site announcement rather than a logo block.

## 2. Color
| Role | Token | Light | Usage |
|------|-------|-------|-------|
| Page | --renatus-page | #F4F6F8 | Public page background |
| Surface | --renatus-surface | #FFFFFF | Cards and shell surfaces |
| Ink | --renatus-ink | #172033 | Headlines and primary text |
| Muted | --renatus-muted | #667085 | Supporting text |
| Accent | --renatus-accent | #2563EB | Primary actions and focus |
| Accent-soft | --renatus-accent-soft | #DBEAFE | Eyebrows and subtle highlights |
| Accent-strong | --renatus-accent-strong | #5B4BE7 | App bar and hero buttons |
| Twilight | --renatus-twilight | #111827 | Image overlay and dashboard ink |
| Glass | --renatus-glass | rgba(255, 255, 255, 0.72) | Translucent auth and app surfaces |
| Glass-tint | --renatus-glass-tint | rgba(91, 75, 231, 0.14) | Colored transparent announcement panel |
| Border | --renatus-border | #E4E7EC | Quiet separation |
| Error | --renatus-error | #B42318 | Validation and API errors |
| Skin stage | --renatus-skin-stage | transparent | WebGL preview canvas with the page surface showing through |
| Skin stage raised | --renatus-skin-stage-raised | #FFFFFF | Preview footer and control rail |
| Skin rail | --renatus-skin-rail | #FFFFFF | Animation controls |
| Skin accent | --renatus-skin-accent | #8D9DFF | Active preview controls and labels |
| Skin glow | --renatus-skin-glow | rgba(111, 129, 255, 0.34) | Canvas halo |
| Skin text | --renatus-skin-text | #172033 | Text around the transparent preview stage |
| Skin grid | --renatus-skin-grid | none | No opaque canvas grid or background |
| Status green | --renatus-status-green | #28B463 | Live/public status |
| Status green strong | --renatus-status-green-strong | #178344 | Public source chip |
| Skin palette | --renatus-skin-palette | #EEF4FF / #7EA6E5 / #40537D / #202B48 | Preview swatches |

## 3. Typography
- Primary: system-ui, -apple-system, "Segoe UI", sans-serif
- Display: same stack, with weight 700 and tight tracking
- Body never falls below 14px.

## 4. Spacing & Layout
- Base unit: 4px.
- Public content max width: 1200px.
- Public home hero is full-bleed, image-led, and keeps the first viewport focused on the brand, description, and up to three actions.
- Auth uses a centered split shell with a translucent announcement panel and a white form panel; it collapses to one column below 768px.
- Authenticated app pages use a bounded document shell with a sticky visual header and scroll owned by the browser document.

## 5. Components
### Public hero
- **Structure**: full-bleed media, overlay, eyebrow, title, description, vertical action stack.
- **States**: loading, error, empty image, configured content.
- **Accessibility**: semantic heading, descriptive image alt, keyboard-accessible links.

### Auth card
- **Structure**: announcement panel beside a validated login/register form.
- **Variants**: login and register.
- **States**: idle, loading, validation error, API error.
- **Accessibility**: labels, field-level validation, live error alert, disabled submit while loading.

### Top navigation shell
- **Structure**: brand, top navigation buttons, and an avatar-only profile trigger in the app bar. The profile popover contains the large avatar, username, nickname, session action, and an admin-panel entry for administrators.
- **Variants**: transparent auth app bar, image-page app bar, and authenticated glass app bar.
- **States**: anonymous, authenticated/closed, authenticated/open, and authenticated-admin/open.
- **Accessibility**: the avatar trigger has a localized accessible name and menu semantics; profile values keep visible labels; menu actions remain keyboard reachable.

### Dashboard panels
- **Structure**: page header, compact metric strip, upload panel, library grid, and player cards.
- **States**: loading, empty, error, busy mutation, and destructive confirmation.
- **Accessibility**: visible labels, CJK-safe wrapping, no icon-only primary navigation.

### Skin detail preview
- **Structure**: MainLayout page built from MudBlazor surfaces, a dark rendering stage on the left, metadata/author card on the right, and a compact action rail below the canvas.
- **States**: demo fallback, public texture loaded, loading/error notice, selected animation, and auto-rotate on/off.
- **Tokens**: transparent canvas, surface `--renatus-surface`, quiet border `--renatus-border`, electric accent `--renatus-skin-accent`, and canvas glow `--renatus-skin-glow`.
- **Accessibility**: canvas has a descriptive label; every icon button has an accessible name; animation controls expose `aria-pressed`; all essential metadata is text, not color-only.

### Admin shell and management pages
- **Structure**: the authenticated app bar switches to an admin-only navigation set with Home, Overview, a Site management category, and an Accounts & resources category. Category menus expose Site settings, Announcements, Users, Players, and Textures. Management pages keep the same top hero card as player management, followed by metric, filter, table, detail, form, and confirmation surfaces.
- **States**: admin overview loading/error/ready, paginated list loading/empty/error, detail/edit dialog, mutation busy/success/error, and destructive confirmation.
- **Accessibility**: admin routes require the Admin role; each management action has an accessible name, table values remain readable without color, and destructive actions require explicit confirmation.

### Admin configuration and announcement editor
- **Structure**: site settings use grouped identity and quick-link forms. Announcement management provides separate login and in-app announcement forms, each with a Markdown input and live rendered preview.
- **States**: loading, load error, editing, saving, saved feedback, and empty Markdown preview.
- **Tokens**: editor preview uses page `--renatus-page`, surface `--renatus-surface`, border `--renatus-border`, and the existing typography/spacing scale.
- **Accessibility**: category menus expose their purpose through localized labels; the editor keeps a visible label and the preview announces content updates without executing raw HTML.

### Account settings
- **Structure**: the account page keeps the shared hero card, then places profile identity/avatar controls beside a password reset form.
- **States**: loading, load error, profile editing, avatar selected/cleared, profile save busy/error, and password reset busy/error/success.
- **Tokens**: profile and password panels use the existing surface, page, border, and accent action tokens.
- **Accessibility**: every account field has a visible label; avatar upload has a labelled file control; password errors and save feedback are announced inline.

### Texture library card grid
- **Structure**: bounded page hero, small stat strip, responsive three-column card grid, and a compact pagination footer.
- **States**: loading, empty, error, public cards, and disabled previous/next page controls.
- **Tokens**: page `--renatus-page`, surface `--renatus-surface`, border `--renatus-border`, accent `--renatus-accent-strong`, and panel shadow `--renatus-shadow-panel`.
- **Accessibility**: each card is a keyboard-accessible link with the texture name, model, source, and uploader available as text; pagination exposes the current page and disabled state.

### Texture upload workbench
- **Structure**: two-column form and live preview, with identity fields, source/public controls, markdown description, single PNG drop zone, and a preview stage that reuses the skin viewer.
- **States**: idle, file selected, invalid file, uploading, upload success, and upload error.
- **Tokens**: white form surface, blue-violet primary action, quiet borders, and the transparent skin stage tokens from the detail preview.
- **Accessibility**: every form control has a visible label; file validation is announced inline; preview updates are described without relying on color alone.

### Verification email
- **Structure**: a centered 600px presentation table with a violet brand rule, site identity, verification heading, one selectable code, expiry guidance, and a quiet footer.
- **Tokens**: existing page, surface, ink, muted, border, and accent-strong colors are copied into inline styles for email clients. Use the system/CJK font stack, 4px spacing units, 14px supporting text, 16px body, 24px heading, and 36px monospace code (32px on narrow screens).
- **States**: template placeholders and populated message; 32px content padding reduces to 24px below 600px. Four to eight digit codes remain readable at 320px. No scripts, animation, web fonts, external images, or required links.
- **Accessibility**: Chinese language declaration, semantic heading, selectable text code, decorative tables marked as presentation, and clear expiry/security guidance. Browser preview verifies layout; inbox-specific rendering requires the sending integration's own client checks.

## 6. Motion & Interaction
### Registration verification
- **Structure**: the existing registration form adds an outlined send-code button below the email field and a labelled six-digit verification field before the nickname/password fields. It reuses MudBlazor controls and existing spacing, surface, accent, and validation styles.
- **States**: empty email, sending, sent with the destination email, resend countdown, send error, invalid/missing code, and registration busy. The send button never submits the registration form; the countdown ends automatically and stops when the component is disposed.
- **Accessibility**: every field has a visible localized label, send results use inline live alerts, and disabled/busy states are communicated in text. The same stacked form layout remains usable on mobile, tablet, and desktop.

- Buttons use MudBlazor's built-in hover, focus, and pressed states plus tokenized CSS transitions for panel lift.
- The profile popover opens from the avatar with MudBlazor's anchored menu transition, closes on outside click or Escape, and keeps the avatar as the stable trigger. Reduced-motion users receive the same menu content without decorative transition emphasis.
- Page surfaces use a short opacity/transform entry only where the existing shell permits it.
- Skin preview actions use a 180ms ease-out color/opacity transition; the selected action uses a persistent indicator, and `prefers-reduced-motion` removes the canvas hint pulse and surface entry transform.
- Reduced-motion users receive the same content without decorative motion.

## 7. Depth & Surface
- Strategy: mixed. Use image overlays for public pages, glass-tinted top bars and announcement panels, white inner form cores, and restrained shadows for dashboard panels. Borders are reserved for form controls and quiet separation.

## 8. Accessibility Constraints & Accepted Debt
- All public imagery receives alt text or an empty alt when decorative.
- API errors remain in a single message property so Todo 6 can replace it with localized keys without changing page flow.
- Top navigation intentionally replaces all drawer affordances; long navigation rows may horizontally scroll on narrow screens rather than introducing a side drawer.
- The texture preview is currently a read-only public detail surface; editing metadata, uploading a replacement, and server-side player association remain outside this slice.
