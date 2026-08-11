# -*- coding: utf-8 -*-
"""
Κάνει τον ΠΡΑΓΜΑΤΙΚΟ κατάλογο ενός μαγαζιού (menu.json) ΒΑΣΙΚΟ ΚΑΤΑΛΟΓΟ της εφαρμογής,
δηλαδή αυτόν από τον οποίο ξεκινά κάθε νέα εγκατάσταση.

    python scripts/menu-to-seed.py C:\\Users\\...\\menu.json

Γράφει το src/PittaPos.Core/Data/MenuSeed.Catalogue.cs. Η λογική (ψωμί, διπλή πίτα, σύνθεση
ονομάτων) μένει στο MenuSeed.cs και δεν την αγγίζει.

ΠΡΟΣΟΧΗ: το menu.json του μαγαζιού το παίρνουμε από το %AppData%\\PittaPos2 του υπολογιστή που
δουλεύει — όχι από παλιό αντίγραφο. Ό,τι γράφεται εδώ αφορά ΜΟΝΟ νέες εγκαταστάσεις· τα μαγαζιά
που ήδη δουλεύουν κρατούν τον δικό τους κατάλογο και δεν επηρεάζονται καθόλου.
"""
import io
import json
import os
import sys
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "PittaPos.Core", "Data", "MenuSeed.Catalogue.cs")


def cs(s):
    """Ελληνικό κείμενο σε C# string literal."""
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def money(v):
    return f"{v:.2f}m"


def cs_list(items):
    return "[" + ", ".join(cs(i) for i in items) + "]"


def main():
    if len(sys.argv) < 2:
        sys.exit("Χρήση: python scripts/menu-to-seed.py <διαδρομή προς menu.json>")
    src = sys.argv[1]
    d = json.load(io.open(src, encoding="utf-8-sig"))

    cats = d["Categories"]
    extras = d["Extras"]
    products = [p for c in cats for p in c["Products"]]

    # Όλα τα προϊόντα έχουν σχεδόν πάντα την ίδια σειρά έξτρα (η σειρά ΜΕΤΡΑΕΙ — τη σέρνει ο ταμίας
    # μέσα στο προϊόν, βλ. CustomizerViewModel). Τη γράφουμε μία φορά αντί για 126.
    orders = {}
    for p in products:
        if p.get("ExtraNames"):
            orders[tuple(p["ExtraNames"])] = orders.get(tuple(p["ExtraNames"]), 0) + 1
    standard = list(max(orders, key=orders.get)) if orders else []

    L = []
    w = L.append
    w("using PittaPos.Core.Models;")
    w("")
    w("namespace PittaPos.Core.Data;")
    w("")
    w("/// <summary>")
    w("/// ΠΑΡΑΓΕΤΑΙ ΑΥΤΟΜΑΤΑ — ΜΗΝ ΤΟ ΓΡΑΦΕΙΣ ΜΕ ΤΟ ΧΕΡΙ.")
    w("///")
    w(f"/// <para>Πηγή: ο πραγματικός κατάλογος ενός μαγαζιού, {len(cats)} κατηγορίες / "
      f"{len(products)} προϊόντα / {len(extras)} έξτρα.")
    w(f"/// Παράχθηκε {datetime.now():%d/%m/%Y}.</para>")
    w("///")
    w("/// <para>Ξαναφτιάξ' το: <c>python scripts/menu-to-seed.py &lt;menu.json&gt;</c> — παίρνοντας το")
    w("/// menu.json από το <c>%AppData%\\PittaPos2</c> του υπολογιστή που δουλεύει.</para>")
    w("/// </summary>")
    w("public static partial class MenuSeed")
    w("{")

    w("    /// <summary>Ο κοινός κατάλογος βασικών υλικών — η προεπιλογή για προϊόντα που δεν έχουν δηλώσει")
    w("    /// δικά τους (βλ. <c>MenuStore.IngredientsFor</c>).</summary>")
    w(f"    public static readonly IReadOnlyList<string> IncludedIngredients =")
    w(f"        {cs_list(d['Ingredients'])};")
    w("")

    w("    /// <summary>Ο κοινός κατάλογος έξτρα με τις τιμές τους. Η σειρά είναι αυτή που βλέπει ο ταμίας.</summary>")
    w("    public static readonly IReadOnlyList<ExtraItem> Extras =")
    w("    [")
    for e in extras:
        w(f"        new() {{ Name = {cs(e['Name'])}, Price = {money(e['Price'])} }},")
    w("    ];")
    w("")

    w("    /// <summary>Επιπλέον χρέωση «διπλή πίτα» ανά κατηγορία.</summary>")
    w("    public static readonly IReadOnlyDictionary<string, decimal> DoublePitaPrices =")
    w("        new Dictionary<string, decimal>")
    w("        {")
    for k, v in d["DoublePitaPrices"].items():
        w(f"            [{cs(k)}] = {money(v)},")
    w("        };")
    w("")

    w("    /// <summary>Τα έξτρα που επιτρέπονται στα περισσότερα προϊόντα, στη σειρά που τα θέλει το")
    w("    /// μαγαζί. Ιδιότητα, όχι πεδίο: κάθε προϊόν παίρνει ΔΙΚΗ ΤΟΥ λίστα, ώστε μια διαγραφή από τη")
    w("    /// Διαχείριση Καταλόγου να μην πειράζει τα υπόλοιπα.</summary>")
    w("    private static List<string> StandardExtras =>")
    w(f"        {cs_list(standard)};")
    w("")

    w("    public static readonly IReadOnlyList<MenuCategory> Categories =")
    w("    [")
    for c in cats:
        w("        new()")
        w("        {")
        flags = ""
        for key in ("HasBread", "FuseBreadIntoName", "SupportsDoublePita"):
            if c.get(key) is not None:
                flags += f", {key} = {'true' if c[key] else 'false'}"
        w(f"            Id = {cs(c['Id'])}, Name = {cs(c['Name'])}{flags},")
        w("            Products =")
        w("            [")
        for p in c["Products"]:
            f = [f"Id = {cs(p['Id'])}", f"Name = {cs(p['Name'])}"]
            if p.get("NameEn"):
                f.append(f"NameEn = {cs(p['NameEn'])}")
            if p.get("PrintName"):
                f.append(f"PrintName = {cs(p['PrintName'])}")
            f.append(f"Price = {money(p['Price'])}")
            if p.get("DeliveryPrice") is not None:
                f.append(f"DeliveryPrice = {money(p['DeliveryPrice'])}")
            if p.get("Customizable"):
                f.append("Customizable = true")
            if p.get("ExtraNames") is not None:
                f.append("ExtraNames = " + ("StandardExtras" if p["ExtraNames"] == standard
                                            else cs_list(p["ExtraNames"])))
            if p.get("Ingredients") is not None:
                f.append(f"Ingredients = {cs_list(p['Ingredients'])}")
            if p.get("Description"):
                f.append(f"Description = {cs(p['Description'])}")
            w("                new() { " + ", ".join(f) + " },")
        w("            ],")
        w("        },")
    w("    ];")
    w("}")
    w("")

    io.open(OUT, "w", encoding="utf-8-sig", newline="\r\n").write("\n".join(L))
    print(f"{os.path.normpath(OUT)}")
    print(f"  {len(cats)} κατηγορίες, {len(products)} προϊόντα, {len(extras)} έξτρα")
    print(f"  {sum(1 for p in products if p.get('Ingredients') is not None)} προϊόντα με δικά τους υλικά")
    print(f"  {sum(1 for p in products if p.get('ExtraNames') == standard)} προϊόντα με την κοινή σειρά έξτρα")


if __name__ == "__main__":
    main()
