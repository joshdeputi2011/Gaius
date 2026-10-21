#include "gaius.h"
#include <cassert>
#include <cstring>
#include <iostream>
#include <string>
std::string calc(const char* expression,const char* cells="{}"){char output[256];gaius_calculate(expression,cells,output,sizeof(output));return output;}
int main(){
 assert(calc("=2+3*4")=="14");assert(calc("=-2^2")=="-4");assert(calc("=2^3^2")=="512");
 assert(calc("=SUM(A1:A2)*2","{\"A1\":\"12\",\"A2\":\"8\"}")=="40");
 assert(calc("=IF(1=1,ROUND(10/3,2),1/0)")=="3.33");
 assert(calc("A1","{\"A1\":\"=B1\",\"B1\":\"=A1\"}")=="#CYCLE!");
 assert(calc("=1/0")=="#DIV/0!");assert(calc("=SUM(A1:ZZ999999)")=="#SIZE!");assert(calc("=unknown(1)")=="#NAME?");
 assert(calc("=1","{bad}")=="#JSON!");assert(calc("=1","{}junk")=="#JSON!");
 char small[3];auto size=gaius_calculate("=12345","{}",small,3);assert(size==6&&std::strcmp(small,"12")==0);
 assert(calc("A1","{\"A1\":\"\\ud83d\\ude00\"}")=="\xf0\x9f\x98\x80");
 assert(calc("A1","{\"A1\":\"\\ud83d\"}")=="#JSON!");
 std::cout<<"Native engine: 15 checks passed\n";
}
