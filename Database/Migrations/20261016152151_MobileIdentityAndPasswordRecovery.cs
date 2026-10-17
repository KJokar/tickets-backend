using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class MobileIdentityAndPasswordRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedEmail",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Email",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                schema: "Identity",
                table: "Users");

            migrationBuilder.EnsureSchema(
                name: "Common");

            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                schema: "Identity",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Mobile",
                schema: "Identity",
                table: "Users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PasswordResetCodeAttempts",
                schema: "Identity",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetCodeExpiresAt",
                schema: "Identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetCodeHash",
                schema: "Identity",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiresAt",
                schema: "Identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                schema: "Identity",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Countries",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsoCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    PhoneCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Flag = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "Common",
                table: "Countries",
                columns: new[] { "Id", "Flag", "IsoCode", "Name", "PhoneCode" },
                values: new object[,]
                {
                    { 1, "/flags/af.svg", "AF", "Afghanistan", "+93" },
                    { 2, "/flags/ax.svg", "AX", "Åland Islands", "+358" },
                    { 3, "/flags/al.svg", "AL", "Albania", "+355" },
                    { 4, "/flags/dz.svg", "DZ", "Algeria", "+213" },
                    { 5, "/flags/as.svg", "AS", "American Samoa", "+1" },
                    { 6, "/flags/ad.svg", "AD", "Andorra", "+376" },
                    { 7, "/flags/ao.svg", "AO", "Angola", "+244" },
                    { 8, "/flags/ai.svg", "AI", "Anguilla", "+1" },
                    { 9, "/flags/aq.svg", "AQ", "Antarctica", "+672" },
                    { 10, "/flags/ag.svg", "AG", "Antigua and Barbuda", "+1" },
                    { 11, "/flags/ar.svg", "AR", "Argentina", "+54" },
                    { 12, "/flags/am.svg", "AM", "Armenia", "+374" },
                    { 13, "/flags/aw.svg", "AW", "Aruba", "+297" },
                    { 14, "/flags/au.svg", "AU", "Australia", "+61" },
                    { 15, "/flags/at.svg", "AT", "Austria", "+43" },
                    { 16, "/flags/az.svg", "AZ", "Azerbaijan", "+994" },
                    { 17, "/flags/bs.svg", "BS", "Bahamas", "+1" },
                    { 18, "/flags/bh.svg", "BH", "Bahrain", "+973" },
                    { 19, "/flags/bd.svg", "BD", "Bangladesh", "+880" },
                    { 20, "/flags/bb.svg", "BB", "Barbados", "+1" },
                    { 21, "/flags/by.svg", "BY", "Belarus", "+375" },
                    { 22, "/flags/be.svg", "BE", "Belgium", "+32" },
                    { 23, "/flags/bz.svg", "BZ", "Belize", "+501" },
                    { 24, "/flags/bj.svg", "BJ", "Benin", "+229" },
                    { 25, "/flags/bm.svg", "BM", "Bermuda", "+1" },
                    { 26, "/flags/bt.svg", "BT", "Bhutan", "+975" },
                    { 27, "/flags/bo.svg", "BO", "Bolivia", "+591" },
                    { 28, "/flags/ba.svg", "BA", "Bosnia and Herzegovina", "+387" },
                    { 29, "/flags/bw.svg", "BW", "Botswana", "+267" },
                    { 30, "/flags/bv.svg", "BV", "Bouvet Island", "+47" },
                    { 31, "/flags/br.svg", "BR", "Brazil", "+55" },
                    { 32, "/flags/io.svg", "IO", "British Indian Ocean Territory", "+246" },
                    { 33, "/flags/vg.svg", "VG", "British Virgin Islands", "+1" },
                    { 34, "/flags/bn.svg", "BN", "Brunei", "+673" },
                    { 35, "/flags/bg.svg", "BG", "Bulgaria", "+359" },
                    { 36, "/flags/bf.svg", "BF", "Burkina Faso", "+226" },
                    { 37, "/flags/bi.svg", "BI", "Burundi", "+257" },
                    { 38, "/flags/cv.svg", "CV", "Cabo Verde", "+238" },
                    { 39, "/flags/kh.svg", "KH", "Cambodia", "+855" },
                    { 40, "/flags/cm.svg", "CM", "Cameroon", "+237" },
                    { 41, "/flags/ca.svg", "CA", "Canada", "+1" },
                    { 42, "/flags/ky.svg", "KY", "Cayman Islands", "+1" },
                    { 43, "/flags/cf.svg", "CF", "Central African Republic", "+236" },
                    { 44, "/flags/td.svg", "TD", "Chad", "+235" },
                    { 45, "/flags/cl.svg", "CL", "Chile", "+56" },
                    { 46, "/flags/cn.svg", "CN", "China", "+86" },
                    { 47, "/flags/cx.svg", "CX", "Christmas Island", "+61" },
                    { 48, "/flags/cc.svg", "CC", "Cocos (Keeling) Islands", "+61" },
                    { 49, "/flags/co.svg", "CO", "Colombia", "+57" },
                    { 50, "/flags/km.svg", "KM", "Comoros", "+269" },
                    { 51, "/flags/cg.svg", "CG", "Congo (Brazzaville)", "+242" },
                    { 52, "/flags/cd.svg", "CD", "Congo (Kinshasa)", "+243" },
                    { 53, "/flags/ck.svg", "CK", "Cook Islands", "+682" },
                    { 54, "/flags/cr.svg", "CR", "Costa Rica", "+506" },
                    { 55, "/flags/ci.svg", "CI", "Côte d'Ivoire", "+225" },
                    { 56, "/flags/hr.svg", "HR", "Croatia", "+385" },
                    { 57, "/flags/cu.svg", "CU", "Cuba", "+53" },
                    { 58, "/flags/cw.svg", "CW", "Curaçao", "+599" },
                    { 59, "/flags/cy.svg", "CY", "Cyprus", "+357" },
                    { 60, "/flags/cz.svg", "CZ", "Czechia", "+420" },
                    { 61, "/flags/dk.svg", "DK", "Denmark", "+45" },
                    { 62, "/flags/dj.svg", "DJ", "Djibouti", "+253" },
                    { 63, "/flags/dm.svg", "DM", "Dominica", "+1" },
                    { 64, "/flags/do.svg", "DO", "Dominican Republic", "+1" },
                    { 65, "/flags/ec.svg", "EC", "Ecuador", "+593" },
                    { 66, "/flags/eg.svg", "EG", "Egypt", "+20" },
                    { 67, "/flags/sv.svg", "SV", "El Salvador", "+503" },
                    { 68, "/flags/gq.svg", "GQ", "Equatorial Guinea", "+240" },
                    { 69, "/flags/er.svg", "ER", "Eritrea", "+291" },
                    { 70, "/flags/ee.svg", "EE", "Estonia", "+372" },
                    { 71, "/flags/sz.svg", "SZ", "Eswatini", "+268" },
                    { 72, "/flags/et.svg", "ET", "Ethiopia", "+251" },
                    { 73, "/flags/fk.svg", "FK", "Falkland Islands", "+500" },
                    { 74, "/flags/fo.svg", "FO", "Faroe Islands", "+298" },
                    { 75, "/flags/fj.svg", "FJ", "Fiji", "+679" },
                    { 76, "/flags/fi.svg", "FI", "Finland", "+358" },
                    { 77, "/flags/fr.svg", "FR", "France", "+33" },
                    { 78, "/flags/gf.svg", "GF", "French Guiana", "+594" },
                    { 79, "/flags/pf.svg", "PF", "French Polynesia", "+689" },
                    { 80, "/flags/tf.svg", "TF", "French Southern Territories", "+262" },
                    { 81, "/flags/ga.svg", "GA", "Gabon", "+241" },
                    { 82, "/flags/gm.svg", "GM", "Gambia", "+220" },
                    { 83, "/flags/ge.svg", "GE", "Georgia", "+995" },
                    { 84, "/flags/de.svg", "DE", "Germany", "+49" },
                    { 85, "/flags/gh.svg", "GH", "Ghana", "+233" },
                    { 86, "/flags/gi.svg", "GI", "Gibraltar", "+350" },
                    { 87, "/flags/gr.svg", "GR", "Greece", "+30" },
                    { 88, "/flags/gl.svg", "GL", "Greenland", "+299" },
                    { 89, "/flags/gd.svg", "GD", "Grenada", "+1" },
                    { 90, "/flags/gp.svg", "GP", "Guadeloupe", "+590" },
                    { 91, "/flags/gu.svg", "GU", "Guam", "+1" },
                    { 92, "/flags/gt.svg", "GT", "Guatemala", "+502" },
                    { 93, "/flags/gg.svg", "GG", "Guernsey", "+44" },
                    { 94, "/flags/gn.svg", "GN", "Guinea", "+224" },
                    { 95, "/flags/gw.svg", "GW", "Guinea-Bissau", "+245" },
                    { 96, "/flags/gy.svg", "GY", "Guyana", "+592" },
                    { 97, "/flags/ht.svg", "HT", "Haiti", "+509" },
                    { 98, "/flags/hm.svg", "HM", "Heard Island and McDonald Islands", "+672" },
                    { 99, "/flags/hn.svg", "HN", "Honduras", "+504" },
                    { 100, "/flags/hk.svg", "HK", "Hong Kong", "+852" },
                    { 101, "/flags/hu.svg", "HU", "Hungary", "+36" },
                    { 102, "/flags/is.svg", "IS", "Iceland", "+354" },
                    { 103, "/flags/in.svg", "IN", "India", "+91" },
                    { 104, "/flags/id.svg", "ID", "Indonesia", "+62" },
                    { 105, "/flags/ir.svg", "IR", "Iran", "+98" },
                    { 106, "/flags/iq.svg", "IQ", "Iraq", "+964" },
                    { 107, "/flags/ie.svg", "IE", "Ireland", "+353" },
                    { 108, "/flags/im.svg", "IM", "Isle of Man", "+44" },
                    { 109, "/flags/il.svg", "IL", "Israel", "+972" },
                    { 110, "/flags/it.svg", "IT", "Italy", "+39" },
                    { 111, "/flags/jm.svg", "JM", "Jamaica", "+1" },
                    { 112, "/flags/jp.svg", "JP", "Japan", "+81" },
                    { 113, "/flags/je.svg", "JE", "Jersey", "+44" },
                    { 114, "/flags/jo.svg", "JO", "Jordan", "+962" },
                    { 115, "/flags/kz.svg", "KZ", "Kazakhstan", "+7" },
                    { 116, "/flags/ke.svg", "KE", "Kenya", "+254" },
                    { 117, "/flags/ki.svg", "KI", "Kiribati", "+686" },
                    { 118, "/flags/xk.svg", "XK", "Kosovo", "+383" },
                    { 119, "/flags/kw.svg", "KW", "Kuwait", "+965" },
                    { 120, "/flags/kg.svg", "KG", "Kyrgyzstan", "+996" },
                    { 121, "/flags/la.svg", "LA", "Laos", "+856" },
                    { 122, "/flags/lv.svg", "LV", "Latvia", "+371" },
                    { 123, "/flags/lb.svg", "LB", "Lebanon", "+961" },
                    { 124, "/flags/ls.svg", "LS", "Lesotho", "+266" },
                    { 125, "/flags/lr.svg", "LR", "Liberia", "+231" },
                    { 126, "/flags/ly.svg", "LY", "Libya", "+218" },
                    { 127, "/flags/li.svg", "LI", "Liechtenstein", "+423" },
                    { 128, "/flags/lt.svg", "LT", "Lithuania", "+370" },
                    { 129, "/flags/lu.svg", "LU", "Luxembourg", "+352" },
                    { 130, "/flags/mo.svg", "MO", "Macau", "+853" },
                    { 131, "/flags/mg.svg", "MG", "Madagascar", "+261" },
                    { 132, "/flags/mw.svg", "MW", "Malawi", "+265" },
                    { 133, "/flags/my.svg", "MY", "Malaysia", "+60" },
                    { 134, "/flags/mv.svg", "MV", "Maldives", "+960" },
                    { 135, "/flags/ml.svg", "ML", "Mali", "+223" },
                    { 136, "/flags/mt.svg", "MT", "Malta", "+356" },
                    { 137, "/flags/mh.svg", "MH", "Marshall Islands", "+692" },
                    { 138, "/flags/mq.svg", "MQ", "Martinique", "+596" },
                    { 139, "/flags/mr.svg", "MR", "Mauritania", "+222" },
                    { 140, "/flags/mu.svg", "MU", "Mauritius", "+230" },
                    { 141, "/flags/yt.svg", "YT", "Mayotte", "+262" },
                    { 142, "/flags/mx.svg", "MX", "Mexico", "+52" },
                    { 143, "/flags/fm.svg", "FM", "Micronesia", "+691" },
                    { 144, "/flags/md.svg", "MD", "Moldova", "+373" },
                    { 145, "/flags/mc.svg", "MC", "Monaco", "+377" },
                    { 146, "/flags/mn.svg", "MN", "Mongolia", "+976" },
                    { 147, "/flags/me.svg", "ME", "Montenegro", "+382" },
                    { 148, "/flags/ms.svg", "MS", "Montserrat", "+1" },
                    { 149, "/flags/ma.svg", "MA", "Morocco", "+212" },
                    { 150, "/flags/mz.svg", "MZ", "Mozambique", "+258" },
                    { 151, "/flags/mm.svg", "MM", "Myanmar", "+95" },
                    { 152, "/flags/na.svg", "NA", "Namibia", "+264" },
                    { 153, "/flags/nr.svg", "NR", "Nauru", "+674" },
                    { 154, "/flags/np.svg", "NP", "Nepal", "+977" },
                    { 155, "/flags/nl.svg", "NL", "Netherlands", "+31" },
                    { 156, "/flags/nc.svg", "NC", "New Caledonia", "+687" },
                    { 157, "/flags/nz.svg", "NZ", "New Zealand", "+64" },
                    { 158, "/flags/ni.svg", "NI", "Nicaragua", "+505" },
                    { 159, "/flags/ne.svg", "NE", "Niger", "+227" },
                    { 160, "/flags/ng.svg", "NG", "Nigeria", "+234" },
                    { 161, "/flags/nu.svg", "NU", "Niue", "+683" },
                    { 162, "/flags/nf.svg", "NF", "Norfolk Island", "+672" },
                    { 163, "/flags/kp.svg", "KP", "North Korea", "+850" },
                    { 164, "/flags/mk.svg", "MK", "North Macedonia", "+389" },
                    { 165, "/flags/mp.svg", "MP", "Northern Mariana Islands", "+1" },
                    { 166, "/flags/no.svg", "NO", "Norway", "+47" },
                    { 167, "/flags/om.svg", "OM", "Oman", "+968" },
                    { 168, "/flags/pk.svg", "PK", "Pakistan", "+92" },
                    { 169, "/flags/pw.svg", "PW", "Palau", "+680" },
                    { 170, "/flags/ps.svg", "PS", "Palestine", "+970" },
                    { 171, "/flags/pa.svg", "PA", "Panama", "+507" },
                    { 172, "/flags/pg.svg", "PG", "Papua New Guinea", "+675" },
                    { 173, "/flags/py.svg", "PY", "Paraguay", "+595" },
                    { 174, "/flags/pe.svg", "PE", "Peru", "+51" },
                    { 175, "/flags/ph.svg", "PH", "Philippines", "+63" },
                    { 176, "/flags/pn.svg", "PN", "Pitcairn Islands", "+64" },
                    { 177, "/flags/pl.svg", "PL", "Poland", "+48" },
                    { 178, "/flags/pt.svg", "PT", "Portugal", "+351" },
                    { 179, "/flags/pr.svg", "PR", "Puerto Rico", "+1" },
                    { 180, "/flags/qa.svg", "QA", "Qatar", "+974" },
                    { 181, "/flags/re.svg", "RE", "Réunion", "+262" },
                    { 182, "/flags/ro.svg", "RO", "Romania", "+40" },
                    { 183, "/flags/ru.svg", "RU", "Russia", "+7" },
                    { 184, "/flags/rw.svg", "RW", "Rwanda", "+250" },
                    { 185, "/flags/bl.svg", "BL", "Saint Barthélemy", "+590" },
                    { 186, "/flags/sh.svg", "SH", "Saint Helena", "+290" },
                    { 187, "/flags/kn.svg", "KN", "Saint Kitts and Nevis", "+1" },
                    { 188, "/flags/lc.svg", "LC", "Saint Lucia", "+1" },
                    { 189, "/flags/mf.svg", "MF", "Saint Martin", "+590" },
                    { 190, "/flags/pm.svg", "PM", "Saint Pierre and Miquelon", "+508" },
                    { 191, "/flags/vc.svg", "VC", "Saint Vincent and the Grenadines", "+1" },
                    { 192, "/flags/ws.svg", "WS", "Samoa", "+685" },
                    { 193, "/flags/sm.svg", "SM", "San Marino", "+378" },
                    { 194, "/flags/st.svg", "ST", "Sao Tome and Principe", "+239" },
                    { 195, "/flags/sa.svg", "SA", "Saudi Arabia", "+966" },
                    { 196, "/flags/sn.svg", "SN", "Senegal", "+221" },
                    { 197, "/flags/rs.svg", "RS", "Serbia", "+381" },
                    { 198, "/flags/sc.svg", "SC", "Seychelles", "+248" },
                    { 199, "/flags/sl.svg", "SL", "Sierra Leone", "+232" },
                    { 200, "/flags/sg.svg", "SG", "Singapore", "+65" },
                    { 201, "/flags/sx.svg", "SX", "Sint Maarten", "+1" },
                    { 202, "/flags/sk.svg", "SK", "Slovakia", "+421" },
                    { 203, "/flags/si.svg", "SI", "Slovenia", "+386" },
                    { 204, "/flags/sb.svg", "SB", "Solomon Islands", "+677" },
                    { 205, "/flags/so.svg", "SO", "Somalia", "+252" },
                    { 206, "/flags/za.svg", "ZA", "South Africa", "+27" },
                    { 207, "/flags/gs.svg", "GS", "South Georgia and the South Sandwich Islands", "+500" },
                    { 208, "/flags/kr.svg", "KR", "South Korea", "+82" },
                    { 209, "/flags/ss.svg", "SS", "South Sudan", "+211" },
                    { 210, "/flags/es.svg", "ES", "Spain", "+34" },
                    { 211, "/flags/lk.svg", "LK", "Sri Lanka", "+94" },
                    { 212, "/flags/sd.svg", "SD", "Sudan", "+249" },
                    { 213, "/flags/sr.svg", "SR", "Suriname", "+597" },
                    { 214, "/flags/sj.svg", "SJ", "Svalbard and Jan Mayen", "+47" },
                    { 215, "/flags/se.svg", "SE", "Sweden", "+46" },
                    { 216, "/flags/ch.svg", "CH", "Switzerland", "+41" },
                    { 217, "/flags/sy.svg", "SY", "Syria", "+963" },
                    { 218, "/flags/tw.svg", "TW", "Taiwan", "+886" },
                    { 219, "/flags/tj.svg", "TJ", "Tajikistan", "+992" },
                    { 220, "/flags/tz.svg", "TZ", "Tanzania", "+255" },
                    { 221, "/flags/th.svg", "TH", "Thailand", "+66" },
                    { 222, "/flags/tl.svg", "TL", "Timor-Leste", "+670" },
                    { 223, "/flags/tg.svg", "TG", "Togo", "+228" },
                    { 224, "/flags/tk.svg", "TK", "Tokelau", "+690" },
                    { 225, "/flags/to.svg", "TO", "Tonga", "+676" },
                    { 226, "/flags/tt.svg", "TT", "Trinidad and Tobago", "+1" },
                    { 227, "/flags/tn.svg", "TN", "Tunisia", "+216" },
                    { 228, "/flags/tr.svg", "TR", "Turkey", "+90" },
                    { 229, "/flags/tm.svg", "TM", "Turkmenistan", "+993" },
                    { 230, "/flags/tc.svg", "TC", "Turks and Caicos Islands", "+1" },
                    { 231, "/flags/tv.svg", "TV", "Tuvalu", "+688" },
                    { 232, "/flags/ug.svg", "UG", "Uganda", "+256" },
                    { 233, "/flags/ua.svg", "UA", "Ukraine", "+380" },
                    { 234, "/flags/ae.svg", "AE", "United Arab Emirates", "+971" },
                    { 235, "/flags/gb.svg", "GB", "United Kingdom", "+44" },
                    { 236, "/flags/us.svg", "US", "United States", "+1" },
                    { 237, "/flags/um.svg", "UM", "United States Minor Outlying Islands", "+1" },
                    { 238, "/flags/uy.svg", "UY", "Uruguay", "+598" },
                    { 239, "/flags/uz.svg", "UZ", "Uzbekistan", "+998" },
                    { 240, "/flags/vu.svg", "VU", "Vanuatu", "+678" },
                    { 241, "/flags/va.svg", "VA", "Vatican City", "+379" },
                    { 242, "/flags/ve.svg", "VE", "Venezuela", "+58" },
                    { 243, "/flags/vn.svg", "VN", "Vietnam", "+84" },
                    { 244, "/flags/wf.svg", "WF", "Wallis and Futuna", "+681" },
                    { 245, "/flags/eh.svg", "EH", "Western Sahara", "+212" },
                    { 246, "/flags/ye.svg", "YE", "Yemen", "+967" },
                    { 247, "/flags/zm.svg", "ZM", "Zambia", "+260" },
                    { 248, "/flags/zw.svg", "ZW", "Zimbabwe", "+263" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_CountryId",
                schema: "Identity",
                table: "Users",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Mobile",
                schema: "Identity",
                table: "Users",
                column: "Mobile",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_IsoCode",
                schema: "Common",
                table: "Countries",
                column: "IsoCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_PhoneCode",
                schema: "Common",
                table: "Countries",
                column: "PhoneCode");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Countries_CountryId",
                schema: "Identity",
                table: "Users",
                column: "CountryId",
                principalSchema: "Common",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Countries_CountryId",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Countries",
                schema: "Common");

            migrationBuilder.DropIndex(
                name: "IX_Users_CountryId",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Mobile",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CountryId",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Mobile",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetCodeAttempts",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetCodeExpiresAt",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetCodeHash",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiresAt",
                schema: "Identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                schema: "Identity",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "Identity",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                schema: "Identity",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                schema: "Identity",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);
        }
    }
}
