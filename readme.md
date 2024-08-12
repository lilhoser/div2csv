# div2csv

## Overview
`div2csv` is an offline web scraper that allows you to extract data from a saved web page and export it as a CSV file. There are many scrapers out there, but this one assumes you will encounter a CAPTCHA or anti-bot page which prevents most online scrapers from working properly.

The typical process is:

* Visit a website with some search results contained on a page with structured data (typically HTML `div`)
* Save the file as HTML. In MS Edge,
  * right-click on the page and click "View page source"
  * right-click and click "Save as"
  * select "Web page, HTML only", pick a location, and click Save
* Manually author a specification for how the `div` elements are structured to get the content you want
* Run this tool to produce a CSV
* Upload the CSV to Google sheets or load into Excel

## Usage

`div2csv [html file] [spec file] [output file]`

* `[html file]` - this is the HTML web page you saved from your browser. It contains the structured data you want to extract.
* `[spec file]` - this is a JSON file containing settings that instruct `div2csv` on the format of the data and how it should process it
* `[output file]` - this is the location of the output CSV

## Creating a specification

`div2csv` relies on a list of column specifications that you must manually create. The column specifications map to individual columns in a result set record from the HTML page you saved. This specification is a JSON file and passed as the second command-line argument to `div2csv`, as shown above.  The format of the specification in c# code:

```
internal class ColumnSpec
{
    public string Name { get; set; }
    public bool Required { get; set; }
    public List<string> XPaths { get; set; }
    public List<string> Strip { get; set; }
}
```

In JSON:

```
{
    "ColumnSpecs": [
    {
        "Name": "root",
        "Required": true,
        "XPaths": [
            "//div[@class='some-div']"
        ],
        "Strip": []
    },
    {
        "Name": "column1",
        "Required": false,
        "XPaths": [
            "//div[@class='a-nested-div']"
        ],
        "Strip": []
    },
      ....
}
```

Creating a specification is a bit of a manual process and you need to understand the basics of XPath. XPath is just a syntax for describing paths to elements in a document tree. Think of it like a file path on your hard drive. W3schools has a [decent tutorial](https://www.w3schools.com/xml/xpath_syntax.asp) on the syntax. We will use XPath to describe the structure of our HTML page, so that `div2csv` can build us a CSV of the records of interest on that page.

### Sample

In the `samples` folder of this repo, there is an HTML page `ha-t1.html` that contains the search results for a type T-1 confederate currency note as retrieved from [this Heritage Auctions search page](https://currency.ha.com/c/search/results.zx?term=7001000&si=2&dept=2021&archive_state=5327&sold_status=1526&sb=3&mode=archive&page=200~1&layout=list). If you open this HTML page in a text editor and search for `<div>` elements, you can see repeating patterns that indicate how these elements are used to structure and present the data. We can use these repeating patterns to define a specification to retrieve all records on the page. The most important pattern is the `<div>` that represents a single search result record. With a little studying, we can see that the `<div>` is `<div class="main-info-container">`. Because this `<div>` represents a single record, we will make it the root of our specification and base all columns with the record on this `<div>`:

```
{
    "ColumnSpecs": [
    {
        "Name": "root",
        "Required": true,
        "XPaths": [
	        "//div[@class='main-info-container']"
        ],
        "Strip": []
    }
]
```

The first column in the record looks to be information about the auction where the item was sold:

```
<div class="main-info-container">
 <div class="item-info">
 <p><a href="https://currency.HA.com/c/search.zx?saleNo=338&ic4=ListView-AuctionNo-051517">
 Auction 338	</a>
	 | Lot 19308 &raquo; Confederate Notes &raquo; 1861 Issues</p>
 <a href="https://currency.ha.com/itm/confederate-notes/1861-issues/t1-1-000-1861-the-montgomery-issue-1-000-was-the-only-type-of-this-denomination-issued-by-the-confederacy-and-was-the-hi/a/338-19308.s?ic4=ListView-ShortDescription-071515" class="item-title short"><b>T1 $1,000 1861.</b> The Montgomery issue $1,000 was the only
type of this denomination issued by the Confederacy, and was the
hi...</a>
```

We'll represent this as a column called "Auction" in our specification and extract the URL:

```
{
    "Name": "Auction",
    "Required": true,
    "XPaths": [
	    "./div[@class='item-info'][1]/p/a[1]"
    ],
    "Strip": []
},
```


Zooming in on the `XPath` field of this `ColumnSpec` indicates we want the XPath search engine to find the first `div` element whose class is set to `item-info` starting from the parser's current location in the document tree (`./` which is relative to `root`) and then return the first anchor `a` element beneath the first paragraph `p` element.

We repeat this process for each data element that can be reliably retrieved from the document structure. It's important to avoid overly-brittle static logic: the more detailed your `XPath` query, the more likely it will be broken by future changes to the website.

The full example is located in `samples\spec.json` and produces a table like the following:

| Auction  | Item    | Highlights | Grader | Grade | Date | Price |
| -------- | ------- | ---------- | ------ | ----- | ---- | ----- |
<a href="https://currency.HA.com/c/search.zx?saleNo=3551&ic4=ListView-AuctionNo-051517">Auction 3551</a> | <a href="https://currency.ha.com/itm/confederate-notes/1861-issues/original-bechtel-album-for-confederate-notes-with-many-rarities/a/3551-20471.s?ic4=ListView-ShortDescription-071515" class="item-title short">Original Bechtel Album for Confederate Notes with ManyRarities</a> |	Featured | <empty> | Superb Gem Crisp Uncirculated |Jan 10, 2017 | $99,875.00
<a href="https://currency.HA.com/c/search.zx?saleNo=3539&ic4=ListView-AuctionNo-051517">Auction 3539</a> | <a href="https://currency.ha.com/itm/confederate-notes/1861-issues/confederate-states-of-america-t1-1861-1000-montgomery-issue-pf-1-cr-1-pcgs-very-fine-35/a/3539-18766.s?ic4=ListView-ShortDescription-071515" class="item-title short">Confederate States of America - T1 1861 $1000 MontgomeryIssue PF-1, Cr. 1. PCGS Very Fine 35.</a>|	Illustrious and Vibrant $1000 Montgomery Note-The Newman-"Colonel" Green Collection Note|	PCGS	|Very Fine 35	|Oct 21, 2015	|$76,375.00
<a href="https://currency.HA.com/c/search.zx?saleNo=3533&ic4=ListView-AuctionNo-051517">Auction 3533</a> | <a href="https://currency.ha.com/itm/confederate-notes/1861-issues/t1-1000-1861-cr-1/a/3533-18527.s?ic4=ListView-ShortDescription-071515" class="item-title short">T1 $1000 1861 Cr. 1</a> | Iconic Montgomery $1000	| PCGS|	Very Fine 35|	Apr 22, 2015	|$58,750.00

Make sure the output of the tool indicates all available records were imported successfully by comparing the number of search results to the number of rows imported by the tool. In the output below, I used the same specification for search results of all T1 through T5 note types:

```
div2csv.exe c:\downloads\ha-t1.html c:\projects\div2csv\samples\spec.json c:\downloads\ha-t1.csv
Loaded specification with 7 columns.
Parsed 66 records from HTML.
CSV saved to c:\downloads\ha-t2.csv

div2csv.exe c:\downloads\ha-t2.html c:\projects\div2csv\samples\spec.json c:\downloads\ha-t2.csv
Loaded specification with 7 columns.
Parsed 47 records from HTML.
CSV saved to c:\downloads\ha-t2.csv

div2csv.exe c:\downloads\ha-t3.html c:\projects\div2csv\samples\spec.json c:\downloads\ha-t3.csv
Loaded specification with 7 columns.
Parsed 64 records from HTML.
CSV saved to c:\downloads\ha-t3.csv

div2csv.exe c:\downloads\ha-t4.html c:\projects\div2csv\samples\spec.json c:\downloads\ha-t4.csv
Loaded specification with 7 columns.
Parsed 65 records from HTML.
CSV saved to c:\downloads\ha-t4.csv

div2csv.exe c:\downloads\ha-t5.html c:\projects\div2csv\samples\spec.json c:\downloads\ha-t5.csv
Loaded specification with 7 columns.
Parsed 163 records from HTML.
CSV saved to c:\downloads\ha-t5.csv
```

# Tips

* It's important to understand that your specification can be broken at any time by changes to the format of the returned search results. Web scraping is by definition a very brittle undertaking.
* To simplify the number of pages you have to process from a search result of interest, make sure all search results fit onto a single page. Sometimes this can be accomplished by specifying maximum search results per page to its highest value.
* Use Google Sheets to import the CSV produced by this tool:
  * Navigate to File->Import
  * Select the Upload tab
  * Upload the CSV file
  * On the Import File screen, select "Insert new sheet" and click "Import Data"
  
# Resources

* An [online XPath validator](https://www.atatus.com/tools/xpath-validator) can help you test your specification