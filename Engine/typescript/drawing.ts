export const shapeKinds = [
  "rectangle",
  "roundedRectangle",
  "ellipse",
  "triangle",
  "rightTriangle",
  "diamond",
  "pentagon",
  "hexagon",
  "star",
  "heart",
  "arrow",
  "leftArrow",
  "doubleArrow",
  "chevron",
  "callout",
  "line",
] as const;
export type ShapeKind = (typeof shapeKinds)[number];
export const shapeLabel = (kind: string) =>
  kind.replace(/([A-Z])/g, " $1").replace(/^./, (c) => c.toUpperCase());
export function shapePath(kind: string): string {
  switch (kind) {
    case "ellipse":
      return "M50 2 A48 48 0 1 0 50 98 A48 48 0 1 0 50 2 Z";
    case "roundedRectangle":
      return "M12 2 H88 Q98 2 98 12 V88 Q98 98 88 98 H12 Q2 98 2 88 V12 Q2 2 12 2 Z";
    case "triangle":
      return "M50 2 L98 98 H2 Z";
    case "rightTriangle":
      return "M2 2 L98 98 H2 Z";
    case "diamond":
      return "M50 2 L98 50 L50 98 L2 50 Z";
    case "pentagon":
      return "M50 2 L98 38 L80 98 H20 L2 38 Z";
    case "hexagon":
      return "M25 2 H75 L98 50 L75 98 H25 L2 50 Z";
    case "star":
      return "M50 2 L62 35 L98 37 L70 59 L80 96 L50 75 L20 96 L30 59 L2 37 L38 35 Z";
    case "heart":
      return "M50 96 C42 84 2 58 2 30 C2 2 36 -8 50 20 C64 -8 98 2 98 30 C98 58 58 84 50 96 Z";
    case "arrow":
      return "M2 30 H62 V2 L98 50 L62 98 V70 H2 Z";
    case "leftArrow":
      return "M98 30 H38 V2 L2 50 L38 98 V70 H98 Z";
    case "doubleArrow":
      return "M2 50 L28 2 V30 H72 V2 L98 50 L72 98 V70 H28 V98 Z";
    case "chevron":
      return "M2 2 H64 L98 50 L64 98 H2 L36 50 Z";
    case "callout":
      return "M2 2 H98 V76 H42 L18 98 V76 H2 Z";
    case "line":
      return "M2 50 H98";
    default:
      return "M2 2 H98 V98 H2 Z";
  }
}
export const shapePreset: Record<string, string> = {
  rectangle: "rect",
  roundedRectangle: "roundRect",
  rightTriangle: "rtTriangle",
  star: "star5",
  arrow: "rightArrow",
  doubleArrow: "leftRightArrow",
  callout: "wedgeRectCallout",
  line: "line",
};
