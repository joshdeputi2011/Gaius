#include "gaius.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <iomanip>
#include <map>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include <cctype>
#include <cstdlib>
namespace {
struct Error:std::runtime_error { using std::runtime_error::runtime_error; };
struct Value { double number=0; bool numeric=true; bool boolean=false; std::string text; std::vector<Value> list; bool range=false; Value(){} Value(double n):number(n){} Value(const std::string& s):numeric(false),text(s){} static Value logical(bool b){Value x(b?1.0:0.0);x.boolean=true;return x;} static Value array(std::vector<Value> v){Value x;x.range=true;x.list=std::move(v);return x;} };
double num(const Value& v){if(v.range)throw Error("#VALUE!");if(v.numeric)return v.number;if(v.text.empty())return 0;char* end=nullptr;double n=std::strtod(v.text.c_str(),&end);if(!end||*end||!std::isfinite(n))throw Error("#VALUE!");return n;}
bool truth(const Value& v){return v.numeric?v.number!=0:!v.text.empty();}
std::string upper(std::string s){for(char& c:s)c=char(std::toupper(static_cast<unsigned char>(c)));return s;}
#include "functions.inc"
std::string jsonString(const std::string& s, size_t& p) {
    while (p < s.size() && std::isspace(static_cast<unsigned char>(s[p]))) ++p;
    if (p >= s.size() || s[p++] != '"') throw Error("#JSON!");
    std::string out;
    auto hex4 = [&]() {
        if (p + 4 > s.size()) throw Error("#JSON!");
        unsigned cp = 0;
        for (int i = 0; i < 4; ++i) {
            char h = s[p++]; int digit;
            if (h >= '0' && h <= '9') digit = h - '0';
            else if (h >= 'a' && h <= 'f') digit = h - 'a' + 10;
            else if (h >= 'A' && h <= 'F') digit = h - 'A' + 10;
            else throw Error("#JSON!");
            cp = cp * 16 + digit;
        }
        return cp;
    };
    while (p < s.size()) {
        char c = s[p++];
        if (c == '"') return out;
        if (static_cast<unsigned char>(c) < 32) throw Error("#JSON!");
        if (c != '\\') { out += c; continue; }
        if (p >= s.size()) throw Error("#JSON!");
        c = s[p++];
        if (c == 'n') out += '\n';
        else if (c == 'r') out += '\r';
        else if (c == 't') out += '\t';
        else if (c == 'b') out += '\b';
        else if (c == 'f') out += '\f';
        else if (c == '"' || c == '\\' || c == '/') out += c;
        else if (c == 'u') {
            unsigned cp = hex4();
            if (cp >= 0xd800 && cp <= 0xdbff) {
                if (p + 2 > s.size() || s[p++] != '\\' || s[p++] != 'u') throw Error("#JSON!");
                unsigned low = hex4();
                if (low < 0xdc00 || low > 0xdfff) throw Error("#JSON!");
                cp = 0x10000 + ((cp - 0xd800) << 10) + low - 0xdc00;
            } else if (cp >= 0xdc00 && cp <= 0xdfff) throw Error("#JSON!");
            if (cp < 128) out += char(cp);
            else if (cp < 2048) { out += char(0xc0 | (cp >> 6)); out += char(0x80 | (cp & 63)); }
            else if (cp < 65536) { out += char(0xe0 | (cp >> 12)); out += char(0x80 | ((cp >> 6) & 63)); out += char(0x80 | (cp & 63)); }
            else { out += char(0xf0 | (cp >> 18)); out += char(0x80 | ((cp >> 12) & 63)); out += char(0x80 | ((cp >> 6) & 63)); out += char(0x80 | (cp & 63)); }
        } else throw Error("#JSON!");
    }
    throw Error("#JSON!");
}
std::map<std::string,std::string> cellsFromJson(const std::string& s){size_t p=0;auto ws=[&](){while(p<s.size()&&std::isspace(static_cast<unsigned char>(s[p])))++p;};ws();if(p>=s.size()||s[p++]!='{')throw Error("#JSON!");std::map<std::string,std::string> cells;ws();if(p<s.size()&&s[p]=='}'){++p;ws();if(p!=s.size())throw Error("#JSON!");return cells;}while(p<s.size()){auto key=jsonString(s,p);ws();if(p>=s.size()||s[p++]!=':')throw Error("#JSON!");auto value=jsonString(s,p);cells[upper(key)]=value;ws();if(p<s.size()&&s[p]=='}'){++p;ws();if(p!=s.size())throw Error("#JSON!");return cells;}if(p>=s.size()||s[p++]!=',')throw Error("#JSON!");}throw Error("#JSON!");}
struct Node {std::string type,value;std::vector<Node> args;};
struct Parser {
 std::vector<std::string> tokens;size_t p=0;
 explicit Parser(const std::string& input){for(size_t i=0;i<input.size();){char c=input[i];if(std::isspace(static_cast<unsigned char>(c))){++i;continue;}if(c=='$'){++i;continue;}if(std::isdigit(static_cast<unsigned char>(c))||c=='.'){char* end=nullptr;std::strtod(input.c_str()+i,&end);size_t j=size_t(end-input.c_str());if(j==i)throw Error("#PARSE!");tokens.push_back(input.substr(i,j-i));i=j;}else if(std::isalpha(static_cast<unsigned char>(c))||c=='_'){std::string t;while(i<input.size()&&(std::isalnum(static_cast<unsigned char>(input[i]))||input[i]=='_'||input[i]=='$')){if(input[i]!='$')t+=input[i];++i;}tokens.push_back(upper(t));}else if(c=='"'){std::string t="\"";++i;bool closed=false;while(i<input.size()){char q=input[i++];if(q=='"'){if(i<input.size()&&input[i]=='"'){t+='"';++i;}else{closed=true;break;}}else t+=q;}if(!closed)throw Error("#PARSE!");tokens.push_back(t+'"');}else{std::string op(1,c);++i;if((c=='<'||c=='>')&&i<input.size()&&(input[i]=='='||(c=='<'&&input[i]=='>')))op+=input[i++];if(std::string("+-*/^(),:=<>").find(c)==std::string::npos)throw Error("#PARSE!");tokens.push_back(op);}if(tokens.size()>10000)throw Error("#SIZE!");}}
 std::string peek(){return p<tokens.size()?tokens[p]:"";}std::string take(){if(p>=tokens.size())throw Error("#PARSE!");return tokens[p++];}
 int prec(const std::string& op){if(op=="="||op=="<>"||op=="<"||op==">"||op=="<="||op==">=")return 1;if(op=="+"||op=="-")return 2;if(op=="*"||op=="/")return 3;if(op=="^")return 4;return -1;}
 Node expr(int minimum=0,int depth=0){if(depth>200)throw Error("#DEPTH!");Node left=primary(depth+1);while(prec(peek())>=minimum){auto op=take();int pr=prec(op);Node right=expr(pr+(op=="^"?0:1),depth+1);left={"binary",op,{left,right}};}return left;}
 Node primary(int depth){auto t=take();if(t=="+"||t=="-")return {"unary",t,{expr(4,depth+1)}};if(t=="("){auto n=expr(0,depth+1);if(take()!=")")throw Error("#PARSE!");return n;}if(t[0]=='"')return {"string",t.substr(1,t.size()-2),{}};if(std::isdigit(static_cast<unsigned char>(t[0]))||t[0]=='.')return {"number",t,{}};if(std::isalpha(static_cast<unsigned char>(t[0]))||t[0]=='_'){if(peek()=="("){take();std::vector<Node> args;if(peek()!=")"){do{args.push_back(expr(0,depth+1));if(peek()!=",")break;take();}while(true);}if(take()!=")")throw Error("#PARSE!");return {"function",t,args};}if(peek()==":"){take();return {"range",t+":"+take(),{}};}return {"ref",t,{}};}throw Error("#PARSE!");}
 Node parse(){auto n=expr();if(p!=tokens.size())throw Error("#PARSE!");return n;}
};
std::pair<int,int> address(const std::string& s){size_t i=0;long long c=0,r=0;while(i<s.size()&&s[i]>='A'&&s[i]<='Z'){c=c*26+s[i++]-'A'+1;if(c>1000000)throw Error("#REF!");}if(!c||i>=s.size()||s[i]=='0')throw Error("#REF!");for(;i<s.size();++i){if(!std::isdigit(static_cast<unsigned char>(s[i])))throw Error("#REF!");r=r*10+s[i]-'0';if(r>10000000)throw Error("#REF!");}if(!r)throw Error("#REF!");return {int(c),int(r)};}
std::string column(int c){std::string s;while(c){s=char('A'+(c-1)%26)+s;c=(c-1)/26;}return s;}
struct Workbook {
 std::map<std::string,std::string> cells;std::set<std::string> active;
 Value cell(const std::string& ref){address(ref);if(active.count(ref))throw Error("#CYCLE!");if(active.size()>200)throw Error("#DEPTH!");auto it=cells.find(ref);if(it==cells.end())return Value("");auto raw=it->second;if(raw.empty())return Value("");if(raw[0]!='='){char* end=nullptr;double n=std::strtod(raw.c_str(),&end);return end&&end!=raw.c_str()&&!*end&&std::isfinite(n)?Value(n):Value(raw);}active.insert(ref);try{auto v=eval(Parser(raw.substr(1)).parse());active.erase(ref);return v;}catch(...){active.erase(ref);throw;}}
 Value eval(const Node& n,int depth=0){if(depth>200)throw Error("#DEPTH!");const auto& v=n.value;auto at=[&](size_t i){return eval(n.args.at(i),depth+1);};if(n.type=="number")return Value(std::strtod(v.c_str(),nullptr));if(n.type=="string")return Value(v);if(n.type=="ref"){if(v=="TRUE")return Value::logical(true);if(v=="FALSE")return Value::logical(false);try{address(v);}catch(...){throw Error("#NAME?");}return cell(v);}if(n.type=="range"){size_t sep=v.find(':');auto a=address(v.substr(0,sep)),b=address(v.substr(sep+1));if(double(std::abs(a.first-b.first)+1)*(std::abs(a.second-b.second)+1)>100000)throw Error("#SIZE!");std::vector<Value> values;for(int r=std::min(a.second,b.second);r<=std::max(a.second,b.second);r++)for(int c=std::min(a.first,b.first);c<=std::max(a.first,b.first);c++)values.push_back(cell(column(c)+std::to_string(r)));return Value::array(values);}if(n.type=="unary")return Value((v=="-"?-1:1)*num(at(0)));if(n.type=="binary"){auto a=at(0),b=at(1);if(v=="="||v=="<>"){bool equal=a.numeric==b.numeric&&a.boolean==b.boolean&&(a.numeric?a.number==b.number:a.text==b.text);return Value::logical(v=="="?equal:!equal);}double x=num(a),y=num(b);if(v=="+")return Value(x+y);if(v=="-")return Value(x-y);if(v=="*")return Value(x*y);if(v=="/"){if(y==0)throw Error("#DIV/0!");return Value(x/y);}if(v=="^")return Value(std::pow(x,y));if(v=="<")return Value::logical(x<y);if(v==">")return Value::logical(x>y);if(v=="<=")return Value::logical(x<=y);if(v==">=")return Value::logical(x>=y);}
 if(n.type=="function"){if(v=="IF"){if(n.args.size()<2||n.args.size()>3)throw Error("#ARGS!");return truth(at(0))?at(1):n.args.size()==3?at(2):Value::logical(false);}if(v=="IFERROR"){if(n.args.size()!=2)throw Error("#ARGS!");try{auto result=at(0);if(result.numeric&&!std::isfinite(result.number))return at(1);return result;}catch(const Error&){return at(1);}}
 if(v=="COUNTIF"||v=="SUMIF"){if(n.args.size()<2||n.args.size()>(v=="COUNTIF"?2:3))throw Error("#ARGS!");auto source=at(0),criterion=at(1),sums=n.args.size()==3?at(2):source;auto values=source.range?source.list:std::vector<Value>{source};auto sv=sums.range?sums.list:std::vector<Value>{sums};if(values.size()!=sv.size())throw Error("#VALUE!");double total=0;for(size_t i=0;i<values.size();i++)if(matchesCriterion(values[i],valueText(criterion)))total+=v=="COUNTIF"?1:sv[i].numeric&&!sv[i].boolean?sv[i].number:0;return Value(total);}
 std::vector<Value> all;for(size_t i=0;i<n.args.size();i++){auto x=at(i);if(x.range)all.insert(all.end(),x.list.begin(),x.list.end());else all.push_back(x);}std::vector<double> numbers;for(const auto& x:all)if(x.numeric&&!x.boolean)numbers.push_back(x.number);double sum=0;for(double x:numbers)sum+=x;if(v=="SUM")return Value(sum);if(v=="COUNT")return Value(double(numbers.size()));if(v=="AVERAGE"){if(numbers.empty())throw Error("#DIV/0!");return Value(sum/numbers.size());}if(v=="MIN")return Value(numbers.empty()?0:*std::min_element(numbers.begin(),numbers.end()));if(v=="MAX")return Value(numbers.empty()?0:*std::max_element(numbers.begin(),numbers.end()));if(v=="ABS"){if(all.size()!=1)throw Error("#ARGS!");return Value(std::abs(num(all[0])));}if(v=="ROUND"){if(all.empty()||all.size()>2)throw Error("#ARGS!");double p=std::pow(10,all.size()==2?num(all[1]):0);double x=num(all[0]);return Value((x<0?-1:1)*std::floor(std::abs(x)*p+0.5)/p);}return extraFunction(v,all);}throw Error("#PARSE!");}
};
std::string calculate(const std::string& expression,const std::string& json){Workbook w;w.cells=cellsFromJson(json);Value value=expression.size()&&expression[0]=='='?w.eval(Parser(expression.substr(1)).parse()):w.cell(upper(expression));if(value.range)throw Error("#VALUE!");if(value.boolean)return value.number!=0?"TRUE":"FALSE";if(!value.numeric)return value.text;if(!std::isfinite(value.number))throw Error("#NUM!");std::ostringstream out;out<<std::setprecision(12)<<value.number;return out.str();}
}
extern "C" size_t gaius_calculate(const char* expression,const char* cells_json,char* output,size_t capacity){std::string result;try{result=calculate(expression?expression:"",cells_json?cells_json:"{}");}catch(const Error& e){result=e.what();}catch(...){result="#ERROR!";}if(output&&capacity){size_t n=std::min(capacity-1,result.size());std::memcpy(output,result.data(),n);output[n]=0;}return result.size()+1;}
extern "C" const char* gaius_version(void){return "0.1.0";}
