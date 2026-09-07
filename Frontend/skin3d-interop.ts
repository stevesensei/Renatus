import {
	CrouchAnimation,
	FlyingAnimation,
	HitAnimation,
	IdleAnimation,
	Render,
	RunningAnimation,
	WalkingAnimation,
	WaveAnimation,
} from "skin3d";
import type { PlayerAnimation } from "skin3d";

const animationNames = ["idle", "walk", "run", "fly", "wave", "crouch", "hit"] as const;
type AnimationName = (typeof animationNames)[number];

type ViewerState = {
	readonly viewer: Render;
	readonly resizeObserver: ResizeObserver;
};

const libraryPreviewModels = ["default", "slim"] as const;
type LibraryPreviewModel = (typeof libraryPreviewModels)[number];

const libraryPreviewWidth = 600;
const libraryPreviewHeight = 400;
const libraryPreviewPaneWidth = libraryPreviewWidth / 2;
const libraryPreviewVerticalOffset = -8;
const libraryPreviewCameraPose = {
	rotation: { x: -0.62, y: 0.534, z: 0.348 },
	position: { x: 30.5, y: 22, z: 42 },
} as const;

const viewers = new Map<HTMLCanvasElement, ViewerState>();
let libraryPreviewRun: Promise<void> | null = null;

function assertNever(value: never): never {
	throw new Error(`Unsupported skin animation: ${String(value)}`);
}

function createAnimation(name: AnimationName): PlayerAnimation {
	switch (name) {
		case "idle":
			return new IdleAnimation();
		case "walk":
			return new WalkingAnimation();
		case "run":
			return new RunningAnimation();
		case "fly":
			return new FlyingAnimation();
		case "wave":
			return new WaveAnimation("right");
		case "crouch":
			return new CrouchAnimation();
		case "hit":
			return new HitAnimation();
		default:
			return assertNever(name);
	}
}

function parseAnimationName(value: string): AnimationName {
	for (const name of animationNames) {
		if (name === value) {
			return name;
		}
	}

	throw new Error(`Unsupported skin animation: ${value}`);
}

function parseLibraryPreviewModel(value: string | undefined): LibraryPreviewModel {
	if (value === libraryPreviewModels[1]) {
		return libraryPreviewModels[1];
	}

	return libraryPreviewModels[0];
}

function createLibraryPreviewViewer(): Render {
	const canvas = document.createElement("canvas");
	return new Render({
		canvas,
		width: libraryPreviewPaneWidth,
		height: libraryPreviewHeight,
		pixelRatio: 1,
		maxPixelRatio: 1,
		preserveDrawingBuffer: true,
		renderPaused: true,
		fov: 42,
		zoom: 0.86,
		enableControls: false,
		allowRotateX: false,
		allowRotateY: false,
		allowZoom: false,
		materialType: "standard",
	});
}

async function renderLibraryPreview(
	viewer: Render,
	image: HTMLImageElement,
	compositeContext: CanvasRenderingContext2D,
	model: LibraryPreviewModel,
	skinSource: string,
): Promise<void> {
	await viewer.loadSkin(skinSource, { model });
	viewer.camera.rotation.set(
		libraryPreviewCameraPose.rotation.x,
		libraryPreviewCameraPose.rotation.y,
		libraryPreviewCameraPose.rotation.z,
	);
	viewer.camera.position.set(
		libraryPreviewCameraPose.position.x,
		libraryPreviewCameraPose.position.y,
		libraryPreviewCameraPose.position.z,
	);
	viewer.playerWrapper.position.set(0, libraryPreviewVerticalOffset, 0);
	viewer.playerWrapper.rotation.set(0, 0, 0);
	viewer.render();
	compositeContext.drawImage(viewer.canvas, 0, 0, libraryPreviewPaneWidth, libraryPreviewHeight);

	viewer.playerWrapper.rotation.y = Math.PI;
	viewer.render();
	compositeContext.drawImage(
		viewer.canvas,
		libraryPreviewPaneWidth,
		0,
		libraryPreviewPaneWidth,
		libraryPreviewHeight,
	);

	image.src = compositeContext.canvas.toDataURL("image/png");
	image.dataset.previewReady = "true";
}

async function renderLibraryPreviewsInternal(root: HTMLElement): Promise<void> {
	const images = Array.from(root.querySelectorAll<HTMLImageElement>("[data-skin-preview]"));
	const pendingImages = images.filter(
		(image) => image.dataset.previewReady !== "true" && image.dataset.previewFailed !== "true",
	);
	if (pendingImages.length === 0) {
		return;
	}

	const compositeCanvas = document.createElement("canvas");
	compositeCanvas.width = libraryPreviewWidth;
	compositeCanvas.height = libraryPreviewHeight;
	const compositeContext = compositeCanvas.getContext("2d");
	if (compositeContext === null) {
		throw new Error("Unable to create the library preview canvas context.");
	}

	const viewer = createLibraryPreviewViewer();
	try {
		for (const image of pendingImages) {
			image.dataset.previewLoading = "true";
			try {
				compositeContext.clearRect(0, 0, libraryPreviewWidth, libraryPreviewHeight);
				const skinSource = image.dataset.skinSource;
				if (skinSource === undefined || skinSource.length === 0) {
					throw new Error("Missing skin source for library preview.");
				}

				await renderLibraryPreview(
					viewer,
					image,
					compositeContext,
					parseLibraryPreviewModel(image.dataset.skinModel),
					skinSource,
				);
			} catch (error) {
				image.dataset.previewFailed = "true";
				if (error instanceof Error) {
					console.warn("Unable to render texture library preview.", error);
				} else {
					console.warn("Unable to render texture library preview.", String(error));
				}
			} finally {
				delete image.dataset.previewLoading;
			}
		}
	} finally {
		viewer.dispose();
	}
}

function resizeViewer(canvas: HTMLCanvasElement, viewer: Render): void {
	const parent = canvas.parentElement;
	if (parent === null) {
		return;
	}

	const { width, height } = parent.getBoundingClientRect();
	viewer.width = Math.max(1, Math.floor(width));
	viewer.height = Math.max(1, Math.floor(height));
	const aspectRatio = width / Math.max(height, 1);
	const responsiveZoom = aspectRatio < 1 ? Math.max(0.54, 0.86 * aspectRatio) : 0.86;
	if (Math.abs(viewer.zoom - responsiveZoom) > 0.01) {
		viewer.zoom = responsiveZoom;
	}
}

export function createViewer(
	canvas: HTMLCanvasElement,
	skinSource: string,
	model: "default" | "slim",
	background?: string,
): void {
	if (viewers.has(canvas)) {
		return;
	}

	const viewer = new Render({
		canvas,
		skin: skinSource,
		model,
		background,
		width: 1,
		height: 1,
		fov: 42,
		zoom: 0.86,
		maxPixelRatio: 2,
		enableControls: true,
		allowRotateX: true,
		allowRotateY: true,
		allowZoom: true,
		materialType: "standard",
	});

	viewer.controls.enablePan = false;
	viewer.globalLight.intensity = 2.4;
	viewer.cameraLight.intensity = 0.8;
	resizeViewer(canvas, viewer);

	const resizeObserver = new ResizeObserver(() => resizeViewer(canvas, viewer));
	if (canvas.parentElement !== null) {
		resizeObserver.observe(canvas.parentElement);
	}

	viewers.set(canvas, { viewer, resizeObserver });
}

export function setAnimation(canvas: HTMLCanvasElement, value: string): void {
	const state = viewers.get(canvas);
	if (state === undefined) {
		return;
	}

	state.viewer.animation = createAnimation(parseAnimationName(value));
}

export async function setSkin(
	canvas: HTMLCanvasElement,
	skinSource: string,
	model: "default" | "slim",
): Promise<void> {
	const state = viewers.get(canvas);
	if (state !== undefined) {
		await state.viewer.loadSkin(skinSource, { model });
	}
}

export function setAutoRotate(canvas: HTMLCanvasElement, enabled: boolean): void {
	const state = viewers.get(canvas);
	if (state !== undefined) {
		state.viewer.autoRotate = enabled;
	}
}

export function disposeViewer(canvas: HTMLCanvasElement): void {
	const state = viewers.get(canvas);
	if (state === undefined) {
		return;
	}

	state.resizeObserver.disconnect();
	state.viewer.dispose();
	viewers.delete(canvas);
}

export async function renderLibraryPreviews(root: HTMLElement): Promise<void> {
	if (libraryPreviewRun !== null) {
		await libraryPreviewRun;
	}

	const run = renderLibraryPreviewsInternal(root);
	libraryPreviewRun = run;
	try {
		await run;
	} finally {
		if (libraryPreviewRun === run) {
			libraryPreviewRun = null;
		}
	}
}
