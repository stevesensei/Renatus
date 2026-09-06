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

const viewers = new Map<HTMLCanvasElement, ViewerState>();

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
