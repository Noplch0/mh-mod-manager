export const defaultAppearance = { theme: "blue", fit: "cover", opacity: 30, blur: 0, positionX: 50, positionY: 50 };

const themes = { blue: "深海蓝", jade: "苍翠绿", copper: "暖铜灰", graphite: "石墨紫" };
const fits = { cover: "填充", contain: "完整", stretch: "拉伸" };
const ranges = [
  ["opacity", "透明度", 70, "%"],
  ["blur", "模糊", 20, " px"],
  ["positionX", "水平位置", 100, "%"],
  ["positionY", "垂直位置", 100, "%"]
];

export function applyAppearance(appearance, backgroundUrl) {
  const root = document.documentElement;
  root.dataset.theme = appearance.theme;
  root.classList.toggle("has-background", Boolean(backgroundUrl));
  if (backgroundUrl) root.style.setProperty("--custom-bg-image", `url("${backgroundUrl}")`);
  else root.style.removeProperty("--custom-bg-image");
  root.style.setProperty("--custom-bg-size", appearance.fit === "stretch" ? "100% 100%" : appearance.fit);
  root.style.setProperty("--custom-bg-opacity", appearance.opacity / 100);
  root.style.setProperty("--custom-bg-blur", `${appearance.blur}px`);
  root.style.setProperty("--custom-bg-position", `${appearance.positionX}% ${appearance.positionY}%`);
}

export function renderAppearance(appearance, hasImage, busy) {
  const disabled = busy ? "disabled" : "";
  return `<section class="appearance-settings" aria-labelledby="appearance-title">
    <h3 id="appearance-title">外观</h3>
    <div class="theme-options" role="group" aria-label="配色">
      ${Object.entries(themes).map(([key, name]) => `<button class="theme-option${appearance.theme === key ? " selected" : ""}" data-swatch="${key}" data-action="appearance-theme" data-value="${key}" aria-pressed="${appearance.theme === key}" ${disabled}><span aria-hidden="true"></span>${name}</button>`).join("")}
    </div>
    <div class="field background-field">
      <span>背景图片</span>
      <div class="actions">
        <input type="file" id="appearance-image" accept="image/png,image/jpeg,image/webp" hidden ${disabled} />
        <button class="btn" data-action="appearance-pick" ${disabled}>${hasImage ? "更换图片" : "选择图片"}</button>
        ${hasImage ? `<button class="btn danger" data-action="appearance-remove" ${disabled}>移除图片</button>` : ""}
      </div>
      ${hasImage ? `
        <div class="background-fits" role="group" aria-label="图片适配">
          ${Object.entries(fits).map(([key, name]) => `<button class="btn" data-action="appearance-fit" data-value="${key}" aria-pressed="${appearance.fit === key}" ${disabled}>${name}</button>`).join("")}
        </div>
        <div class="background-ranges">
          ${ranges.map(([key, name, max, unit]) => `<label class="background-range">
            <span>${name}</span><output>${appearance[key]}${unit}</output>
            <input type="range" data-appearance="${key}" data-unit="${unit}" min="0" max="${max}" value="${appearance[key]}" ${disabled} />
          </label>`).join("")}
        </div>` : ""}
    </div>
    <button class="btn" data-action="appearance-reset" ${disabled}>恢复默认外观参数</button>
  </section>`;
}

export async function readBackgroundImage(file) {
  if (file.size > 8 * 1024 * 1024) throw new Error("背景图片不能超过 8 MB。");
  if (file.type && !["image/png", "image/jpeg", "image/webp"].includes(file.type)) {
    throw new Error("请选择 PNG、JPEG 或 WebP 图片。");
  }
  let image;
  try {
    image = await createImageBitmap(file);
  } catch {
    throw new Error("无法读取图片，请选择有效的 PNG、JPEG 或 WebP 图片。");
  }
  image.close();
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result.split(",", 2)[1]);
    reader.onerror = () => reject(new Error("无法读取背景图片。"));
    reader.readAsDataURL(file);
  });
}
