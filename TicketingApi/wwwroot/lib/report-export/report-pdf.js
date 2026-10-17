import { formatPersianDate } from "./persian-date";
/** Render Persian text with the browser's loaded font, preserving RTL shaping in the PDF. */
export async function createReportPdf(report, agentName, rows) {
    await document.fonts.ready;
    const { PDFDocument } = await import("./pdf-lib.esm.min.js");
    const canvas = document.createElement("canvas");
    const width = 1684, height = 1190, scale = 2;
    canvas.width = width * scale;
    canvas.height = height * scale;
    const ctx = canvas.getContext("2d");
    if (!ctx)
        throw new Error("PDF canvas is unavailable");
    ctx.scale(scale, scale);
    ctx.fillStyle = "#fff";
    ctx.fillRect(0, 0, width, height);
    const font = getComputedStyle(document.body).fontFamily;
    function text(value, x, y, size = 22, color = "#183144", align = "right", bold = false) {
        ctx.font = `${bold ? "700" : "400"} ${size}px ${font}`;
        ctx.direction = "rtl";
        ctx.textAlign = align;
        ctx.fillStyle = color;
        ctx.fillText(value, x, y, width - 150);
    }
    function box(x, y, w, h) {
        ctx.fillStyle = "#f8fbfe";
        ctx.strokeStyle = "#dce7ef";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.roundRect(x, y, w, h, 18);
        ctx.fill();
        ctx.stroke();
    }
    const number = (value) => value.toLocaleString("fa-IR");
    text("گزارش درخواست‌های پذیرفته‌شده", width - 80, 92, 40, "#183144", "right", true);
    text(`پشتیبان: ${agentName}`, width - 80, 144, 24);
    text(`${formatPersianDate(report.from)} تا ${formatPersianDate(report.to)} · ساعت تهران`, width - 80, 189, 22, "#718296");
    const stats = [["کل درخواست‌ها", report.summary.total], ["در جریان", report.summary.inProgress], ["پاسخ داده‌شده", report.summary.answered], ["بسته‌شده", report.summary.closed]];
    stats.forEach(([label, count], index) => {
        const x = width - 80 - (index + 1) * 366 - index * 20;
        box(x, 234, 366, 126);
        text(label, x + 338, 277, 22, "#718296");
        text(number(count), x + 338, 330, 38, "#183144", "right", true);
    });
    box(80, 402, 492, 604);
    box(596, 402, 1008, 604);
    text("توزیع وضعیت درخواست‌ها", 542, 451, 25, "#183144", "right", true);
    text("روند ثبت درخواست‌های پذیرفته‌شده", 1574, 451, 25, "#183144", "right", true);
    const states = [
        { label: "در جریان", count: report.summary.inProgress, color: "#d49b32" },
        { label: "پاسخ داده‌شده", count: report.summary.answered, color: "#9274ca" },
        { label: "بسته‌شده", count: report.summary.closed, color: "#30a18a" },
    ];
    ctx.strokeStyle = "#e4edf4";
    ctx.lineWidth = 42;
    ctx.beginPath();
    ctx.arc(326, 636, 110, 0, Math.PI * 2);
    ctx.stroke();
    let angle = -Math.PI / 2;
    for (const state of states) {
        if (!state.count || !report.summary.total)
            continue;
        const end = angle + state.count / report.summary.total * Math.PI * 2;
        ctx.strokeStyle = state.color;
        ctx.beginPath();
        ctx.arc(326, 636, 110, angle, end);
        ctx.stroke();
        angle = end;
    }
    text(number(report.summary.total), 326, 640, 38, "#183144", "center", true);
    text("درخواست", 326, 674, 18, "#718296", "center");
    states.forEach((state, index) => {
        const y = 828 + index * 57;
        ctx.fillStyle = state.color;
        ctx.fillRect(520, y - 18, 16, 16);
        text(state.label, 500, y, 21);
        text(number(state.count), 170, y, 23, "#183144", "left", true);
    });
    const days = report.dailyCreated, ceiling = Math.max(4, Math.ceil(Math.max(0, ...days.map(day => day.count)) / 4) * 4);
    const left = 676, right = 1558, bottom = 898, plotHeight = 360, barWidth = (right - left) / Math.max(1, days.length);
    for (let step = 0; step <= 4; step++) {
        const y = bottom - step / 4 * plotHeight;
        ctx.strokeStyle = "#dce7ef";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(left, y);
        ctx.lineTo(right, y);
        ctx.stroke();
        text(number(ceiling * step / 4), left - 18, y + 7, 18, "#718296");
    }
    days.forEach((day, index) => {
        const x = left + index * barWidth, h = day.count / ceiling * plotHeight;
        ctx.fillStyle = "#3174cc";
        ctx.fillRect(x + barWidth * .16, bottom - h, Math.max(1, barWidth * .68), h);
        if (index % Math.max(1, Math.ceil(days.length / 7)) === 0 || index === days.length - 1)
            text(formatPersianDate(day.date).slice(5), x + barWidth / 2, bottom + 35, 17, "#718296", "center");
    });
    const generatedAt = new Intl.DateTimeFormat("fa-IR", { dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Tehran" }).format(new Date(report.generatedAt));
    text(`زمان تهیهٔ گزارش: ${generatedAt}`, width - 80, 1074, 20, "#718296");
    text("این گزارش فقط شامل درخواست‌های پذیرفته‌شدهٔ همین پشتیبان است.", width - 80, 1120, 20, "#718296");
    const pdf = await PDFDocument.create();
    pdf.setTitle("گزارش درخواست‌های پذیرفته‌شده");
    const image = await pdf.embedPng(canvas.toDataURL("image/png"));
    const page = pdf.addPage([841.89, 595.28]);
    page.drawImage(image, { x: 0, y: 0, width: 841.89, height: 595.28 });
    function wrap(value, maxWidth) {
        ctx.font = `400 22px ${font}`;
        const lines = [];
        let line = "";
        for (const word of value.split(/\s+/).filter(Boolean)) {
            const candidate = line ? line + " " + word : word;
            if (ctx.measureText(candidate).width <= maxWidth) {
                line = candidate;
                continue;
            }
            if (line) {
                lines.push(line);
                line = "";
            }
            for (const character of word) {
                if (line && ctx.measureText(line + character).width > maxWidth) {
                    lines.push(line);
                    line = "";
                }
                line += character;
            }
        }
        if (line)
            lines.push(line);
        return lines.length ? lines : [""];
    }
    let index = 0, detailPage = 0;
    while (rows && index < rows.length) {
        detailPage++;
        ctx.fillStyle = "#fff";
        ctx.fillRect(0, 0, width, height);
        text("فهرست درخواست‌های پذیرفته‌شده", width - 80, 92, 36, "#183144", "right", true);
        text(`پشتیبان: ${agentName}`, width - 80, 144, 24);
        text(`${formatPersianDate(report.from)} تا ${formatPersianDate(report.to)} · ساعت تهران`, width - 80, 189, 22, "#718296");
        ctx.fillStyle = "#edf4fa";
        ctx.fillRect(80, 220, width - 160, 52);
        text("ردیف", 1560, 254, 22, "#183144", "center", true);
        text("نام مشتری", 1470, 254, 22, "#183144", "right", true);
        text("عنوان درخواست", 1030, 254, 22, "#183144", "right", true);
        let y = 272;
        while (index < rows.length) {
            const row = rows[index], customer = wrap(row.customerName, 390), title = wrap(row.title, 920);
            const rowHeight = Math.max(58, Math.max(customer.length, title.length) * 30 + 22);
            if (y + rowHeight > 1050)
                break;
            text(number(index + 1), 1560, y + 35, 22, "#718296", "center");
            customer.forEach((line, n) => text(line, 1470, y + 35 + n * 30));
            title.forEach((line, n) => text(line, 1030, y + 35 + n * 30));
            y += rowHeight;
            ctx.strokeStyle = "#e4edf4";
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(80, y);
            ctx.lineTo(width - 80, y);
            ctx.stroke();
            index++;
        }
        text(`فهرست درخواست‌ها · صفحه ${number(detailPage)} · ${number(rows.length)} درخواست`, width - 80, 1120, 20, "#718296");
        const detailImage = await pdf.embedPng(canvas.toDataURL("image/png"));
        pdf.addPage([841.89, 595.28]).drawImage(detailImage, { x: 0, y: 0, width: 841.89, height: 595.28 });
        await new Promise(resolve => setTimeout(resolve, 0));
    }
    return pdf.save();
}
export async function downloadReportPdf(report, agentName, rows) {
    const bytes = await createReportPdf(report, agentName, rows);
    const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)], { type: "application/pdf" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `tickets-report-${report.from}-${report.to}.pdf`;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
