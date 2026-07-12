# POST /List
This API returns a list of account letters for the requested member.

* **URL - API/V2/ExampleModuleName/list**
* Method - POST 
* Content Type - JSON
* Roles - CSR, Member
* Data Source - DG Webde EXAMPLE v3.0.0

### Request
#### Headers
| Name	     | Required	| Format	| Description|
| ---------- |:-------------:| -----:| ---|
| Client-Id  |	Y	| String	| Required field. Example values: AppA or AppB|
| Session-Id |	N	| String	| Strongly Recommended field to track consumer session. For example: From AppA, pass the HttpContext.TraceIdentifier value. If none is provided, a GUID will be generated internally.|
| Role  |	Y	| String	| Required field. |
| UserValue  |	Y	| String	| Required field. The AD Username (if AppA) or the AD service username on IIS AppPool (if AppB)|

#### Body
When using the 'LIST' endpoint to do XYZ, there are three ways to search: 1) example1 2) example2 and 3) example 3.

For all three search types, you can optionally provide a From and TO Date to further filter the results. If you provide a FromDate, then you must also include a ToDate and vice versa.
Request is the following: 

|Field Name	|    Description|
|---|---|
|MemberID|MemberId must not be null or empty. This is ALWAYS required, even when filtering by ClaimNumber, because we still want this for logging purposes|
|DepNo|DepNo must be greater than 0 when Filter is "Member" only. Always required, even when filtering by claim number, where it's still used for logging.|
|ClaimNumbers| Optional way to filter. Pass one or many string values. If 'FilterType' value is CLAIM, this value MUST be present. Otherwise it can be left empty. Only way to get 'HIST2' claim letter records from DG is if you search by this field!|
|FromDate|Optional value that if present will automatically limit the returns results to Fulfilled only, and exclude 'requested' or 'in-process' status records. This is because this date field only filters on 'FulFilled Date'.  If this FromDate is present, ToDate must also be present!|
|ToDate|Optional value that if present will automatically limit the returns results to Fulfilled only, and exclude 'requested' or 'in-process' status records. This is because this date field only filters on 'FulFilled Date'. If this ToDate is present, FromDate must also be present!|
|FilterType|ALL or MEMBER or CLAIM. Note that if CLAIM is passed in, then 'ClaimNumber' value will be expected. Otherwise if ALL or MEMBER, the MemberId and Depno values will be expected|	
|IncludeHasRead (coming soon!)|If null, defaults to 'false'. If true, internal logic will load any related 'MessageReadTracking' data and update the 'HasRead' property for each row in returned list. Primarily used by MC Correspondence Center page.|	
|UserGUID (coming soon!)|If 'IncludeHasRead' is true above, this can't be NULL. Example would ge MC objectGUID unique id for a MC user from the cust.meritain.com AD. Example value: 550e8400-e29b-41d4-a716-446655440000|	


```
{
“MemberId”:”123456789”,
“Depno”:”0”,
"ClaimNumbers": [],
“Filter”:”ALL”, 
},

or

{
“MemberId”:”123456789”,
“Depno”:”2”,
"ClaimNumbers": [],
“Filter”:”MEMBER”, 
},

or

{
“MemberId”:”123456789”,
“Depno”:”2”,
"ClaimNumbers": ["A78924J2"], 
“Filter”:”CLAIM”, 
},

or

{
“MemberId”:”123456789”,
“Depno”:”2”,
"ClaimNumbers": ["A78924J2", "A78959B1"], 
"FromDate":”2023-01-01”,
"ToDate":”2023-12-31”,"
“Filter”:”CLAIM”, 
},

```
### Response
#### Body

```
{
    “ExampleList”: [
     {
       “Id”:””, (string datatype in response for v2 endpoint)
       “MemberID”: “”,
       “DepNo”:””,
       “RecipientType”:””, (EMPLOYEE, DEPENDENT or CLAIM)
       “Source”:””, (LETTER.COPIES, CORRESP.TRACKING OR HIST2)
       “Status”:””, (REQUESTED, IN-PROGRESS or FULFILLED)
       “Date”:””, (Type of date matches up with 'Status' value in above property. 'FULFILLED' date, for ex.)
       “Time”:””.
       “DeliveryMethod”:””, (MAIL, EMAIL, FAX, VIEW, LOOKUP)
       “DocumentName”:””,
       “InputDefinition”:””, (CORRESP.INPUT.DEF.ID for CORRESP.TRACKING records, FILE for LETTER.COPIES records)
       “LetterLineNumber”:””, (Location of the doc in H2LETTERS listing. Needed when file 'source' is HIST2)
       “DCN”:”[string, string]”,
       “SendOptions”: [
       {
         “SendOption”:”” (MAIL, EMAIL, DOWNLOAD, RESTRICTED)
       }]
    }]
}

or

{
    “ExampleList”: [
     {
       “Id”:””, (string datatype in response for v2 endpoint)
       “MemberID”: “”,
       “DepNo”:””,
       “RecipientType”:””, (EMPLOYEE, DEPENDENT or CLAIM)
       “Source”:””, (LETTER.COPIES, CORRESP.TRACKING OR HIST2)
       “Status”:””, (REQUESTED, IN-PROGRESS or FULFILLED)
       “Date”:””, (Type of date matches up with 'Status' value in above property. 'FULFILLED' date, for ex.)
       “Time”:””.
       “DeliveryMethod”:””, (MAIL, EMAIL, FAX, VIEW, LOOKUP)
       “DocumentName”:””,
       “InputDefinition”:””, (CORRESP.INPUT.DEF.ID for CORRESP.TRACKING records, FILE for LETTER.COPIES records)
       “LetterLineNumber”:””, (Location of the doc in H2LETTERS listing. Needed when file 'source' is HIST2)
       “DCN”:”[string, string]”,
       “SendOptions”: [
       {
         “SendOption”:”” (MAIL, EMAIL, DOWNLOAD, RESTRICTED)
       }]
      “referenceFile”:””, (Name of the file associated with this letter. Valid values: HISTORY.ALL)
      “referenceID”:””, (Key to the file in the ReferenceFile output. For HISTORY.ALL, this is a claim number.)
    }]
}
```
### Error Code
|Code	|        Description|
|---|---|
|400|	        Bad Request. |
|400|	        Missing client-id. |
|500	|        Internal server error. See logs|
|404|    No letters found.|


```
{
    "status": 400,
    "message": "Bad request."
}

{
  "Status": 400,
  "Message": "Missing client-id."
}

{
  "Status": 404,
  "Message": "No letters found."
}
{
    "status": 500,
    "message": "An error occurred. Please review the logs for more information."
}
```
# POST /Detail
This API returns the tracking information for and account letter.

* **URL - API/V2/ExampleModuleName/Detail**
* Method - POST 
* Content Type - JSON
* Roles - CSR, Member
* Data Source - DG Webde XYZMethodHere

### Request
#### Headers
| Name	     | Required	| Format	| Description|
| ---------- |:-------------:| -----:| ---|
| Client-Id  |	Y	| String	| Required field. Example values: AppA or AppB|
| Session-Id |	N	| String	| Strongly Recommended field to track consumer session. For example: From AppA, pass the HttpContext.TraceIdentifier value. If none is provided, a GUID will be generated internally.|
| Role  |	Y	| String	| Required field. |
| UserValue  |	Y	| String	| Required field. The AD Username (if AppA) or the AD service username on IIS AppPool (if AppB)|



#### Body
Use the 'DETAIL' endpoint to retrieve specific correspondence. 

Request is the following: 

|Field Name	|    Description|
|---|---|
|CorrespondenceKey|CorrespondenceKey must not be null or empty. This is required.|
|CorrespondenceFile|CorrespondenceFile must not be null or empty. This is required. Seomtiems called 'Source'. Valid values include: CORRESP.TRACKING or HIST2|
|LetterLineNumber|LetterLineNumber is only requierd when the CorrespondenceFile value is 'HIST2'|
|MemberID|MemberId must not be null or empty. This is required. If request is from AppA app, the memberId and depno are used with a GetCorrespondenceSummary call to check restricted access. Otherwise it's just used with logging/exception details. |
|DepNo|DepNo must not be null or empty. This is required. If request is from AppA app, the memberId and depno are used with a GetCorrespondenceSummary call to check restricted access. Otherwise it's just used with logging/exception details. |


```
{
    “CorrespondenceKey”:”78945612”,
    “CorrespondenceFile”:”CORRESP.TRACKING”,
    “MemberID”:”1234567879”, 
    “DepNo”:”1”,  
}

or

{
    “CorrespondenceKey”:”B5354D2”,
    “CorrespondenceFile”:”HIST2”,
    "LetterLineNumber":"3",
    “MemberID”:”1234567879”, 
    “DepNo”:”1”,  
}
```
### Response
#### Body
TODO: Need to update below to include new fields. This currently shows response from GetCorrespondencTrackingInfo.
```
{
    “AccountLetter”:
   {
     “Id”:””,
     “InputDefinition”: “”,
     “RecipientType”: “”,
     “MemberID”: “”,
     “DepNo”:””,
     “RecipientType”:””,
     “DeliveryMethod”:””,
     “DateRequested”:””,
     “TimeRequested”:””,
     “RequestSource”:””,
     “RequestSourceID”:””,
     “DocumentName”:””,
     “CorrespondenceVendor”:””,
     “DeliveryInfos”: {
         “DeliveryInfo”: {
                  “DateSent”:””,
                  “TimeSent”:””,
                  “SentTo”:””,
                  “ExtraFileName”:””,
                  “DateReturned”:””,
                  “TimeReturned”:””,
                  “ResponseCode”:””, (CREATED, ERROR)
                              “ResponseDetails”:””
          }
     }
     “Fulfillments”: {
     “Fulfillment”: {
          “DateFulfilled”:””,
          “TimeFulfilled”:””,
          “DeliveryMethod”:””,
          “FulfillmentInfos”: {
               “FulfillmentInfoAtrribute”: {
                    “AttributeName”:””,
                    “AttributeValue”:””
                    }
                }
          }
     }
     “Details”: {
          “DetailAttribute”: {
               “AttributeName”:””,
               “AttributeValue”
               }
          }
     }
}
```
### Error Code
|Code	|        Description|
|---|---|
|400|	        Bad Request. |
|400|	        Missing client-id. |
|401|           Unauthorized  |
|404|           Not found|
|500	|        Internal server error. See logs|


```
{
    "status": 400,
    "message": "Bad request."
}

{
  "Status": 400,
  "Message": "Missing client-id."
}

{
  "Status": 401,
  "Message": "Unauthorized CSR"
}

{
  "Status": 404,
  "Message": "Not Found"
}

{
    "status": 500,
    "message": "An error occurred. Please review the logs for more information."
}
```
