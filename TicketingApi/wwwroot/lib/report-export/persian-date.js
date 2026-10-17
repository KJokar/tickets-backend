export const persianMonths = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
const formatter = new Intl.DateTimeFormat("en-US-u-ca-persian", { year: "numeric", month: "numeric", day: "numeric", timeZone: "UTC" });
const dayMilliseconds = 86_400_000;
export function toPersianDate(iso) {
    const parts = formatter.formatToParts(new Date(iso + "T12:00:00Z"));
    const get = (key) => Number(parts.find(part => part.type === key)?.value);
    return { year: get("year"), month: get("month"), day: get("day") };
}
export function fromPersianDate(year, month, day) {
    if (![year, month, day].every(Number.isInteger) || year < 1200 || year > 1600 || month < 1 || month > 12 || day < 1 || day > 31)
        return "";
    const key = (y, m, d) => y * 372 + m * 31 + d;
    const target = key(year, month, day);
    let low = Math.floor(Date.UTC(year + 621, 0, 1) / dayMilliseconds);
    let high = Math.floor(Date.UTC(year + 622, 11, 31) / dayMilliseconds);
    while (low <= high) {
        const middle = Math.floor((low + high) / 2);
        const iso = new Date(middle * dayMilliseconds).toISOString().slice(0, 10);
        const value = toPersianDate(iso);
        const actual = key(value.year, value.month, value.day);
        if (actual === target)
            return iso;
        if (actual < target)
            low = middle + 1;
        else
            high = middle - 1;
    }
    return "";
}
export function formatPersianDate(iso) {
    const { year, month, day } = toPersianDate(iso);
    const digits = (value, width = 2) => value.toLocaleString("fa-IR", { useGrouping: false, minimumIntegerDigits: width });
    return `${digits(year, 4)}/${digits(month)}/${digits(day)}`;
}
export function persianMonthLength(year, month) {
    const first = fromPersianDate(year, month, 1);
    const next = fromPersianDate(month === 12 ? year + 1 : year, month === 12 ? 1 : month + 1, 1);
    return (Date.parse(next) - Date.parse(first)) / dayMilliseconds;
}
